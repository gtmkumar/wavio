using System.Text.Json;
using WaAdmin.Application.Templates.Commands.ProcessTemplateCategoryChanged;
using WaAdmin.Application.Templates.Commands.ProcessTemplateStatusChanged;
using WaPlatform.Contracts.IntegrationEvents.V1;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace WaAdmin.Infrastructure.Messaging;

/// <summary>
/// Consumes <c>wa.template.status_changed.v1</c> / <c>wa.template.category_changed.v1</c> from
/// the shared <c>wavio.events</c> topic exchange (published by wa-ingest-svc) and dispatches the
/// matching Application command per message (issue #16 Tasks 3-4).
///
/// One DI scope per message: <see cref="ScopedCurrentTenant"/> is set to the event's TenantId
/// before the scope's <see cref="IDispatcher"/> touches any tenant-scoped table, so RLS applies
/// correctly even though there is no HTTP request driving this scope (see
/// <see cref="ScopedCurrentTenant"/>'s doc comment).
///
/// Manual ack: a handler returning <c>false</c> ("parked" — unresolvable tenant, unknown
/// template, or invalid transition; see the command handlers) or throwing is Nacked without
/// requeue, landing in <see cref="DeadLetterQueueName"/> via the queue's
/// <c>x-dead-letter-exchange</c> argument — never silently dropped, never requeued into a hot
/// retry loop for a message that will never succeed.
/// </summary>
public sealed partial class TemplateEventsConsumerBackgroundService : BackgroundService
{
    public const string ExchangeName = "wavio.events";
    public const string QueueName = "wa-admin.template-events";
    public const string DeadLetterExchangeName = "wavio.events.dlx";
    public const string DeadLetterQueueName = "wa-admin.template-events.dlq";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RabbitMqConnectionManager _connectionManager;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TemplateEventsConsumerBackgroundService> _logger;

    public TemplateEventsConsumerBackgroundService(
        RabbitMqConnectionManager connectionManager, IServiceScopeFactory scopeFactory,
        ILogger<TemplateEventsConsumerBackgroundService> logger)
    {
        _connectionManager = connectionManager;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogConsumerLoopFailed(_logger, ex);
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        var channel = await _connectionManager.CreateChannelAsync(stoppingToken);

        await channel.ExchangeDeclareAsync(
            ExchangeName, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: stoppingToken);
        await channel.ExchangeDeclareAsync(
            DeadLetterExchangeName, ExchangeType.Fanout, durable: true, autoDelete: false, cancellationToken: stoppingToken);

        await channel.QueueDeclareAsync(
            DeadLetterQueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await channel.QueueBindAsync(
            DeadLetterQueueName, DeadLetterExchangeName, routingKey: string.Empty, cancellationToken: stoppingToken);

        var queueArgs = new Dictionary<string, object?> { ["x-dead-letter-exchange"] = DeadLetterExchangeName };
        await channel.QueueDeclareAsync(
            QueueName, durable: true, exclusive: false, autoDelete: false, arguments: queueArgs,
            cancellationToken: stoppingToken);
        await channel.QueueBindAsync(
            QueueName, ExchangeName, routingKey: TemplateStatusChangedV1.Name, cancellationToken: stoppingToken);
        await channel.QueueBindAsync(
            QueueName, ExchangeName, routingKey: TemplateCategoryChangedV1.Name, cancellationToken: stoppingToken);

        await channel.BasicQosAsync(0, prefetchCount: 10, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, ea) => HandleDeliveryAsync(channel, ea, stoppingToken);

        await channel.BasicConsumeAsync(QueueName, autoAck: false, consumer, cancellationToken: stoppingToken);

        // Idle until cancellation or the channel dies; ExecuteAsync's outer loop reconnects.
        while (!stoppingToken.IsCancellationRequested && channel.IsOpen)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    private async Task HandleDeliveryAsync(IChannel channel, BasicDeliverEventArgs ea, CancellationToken stoppingToken)
    {
        var routingKey = ea.RoutingKey;
        var processed = false;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ScopedCurrentTenant>();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

            if (routingKey == TemplateStatusChangedV1.Name)
            {
                var evt = JsonSerializer.Deserialize<TemplateStatusChangedV1>(ea.Body.Span, JsonOptions)
                    ?? throw new JsonException("Empty body for wa.template.status_changed.v1");
                tenantContext.OverrideTenantId = evt.TenantId;
                processed = await dispatcher.SendAsync(new ProcessTemplateStatusChangedCommand(evt), stoppingToken);
            }
            else if (routingKey == TemplateCategoryChangedV1.Name)
            {
                var evt = JsonSerializer.Deserialize<TemplateCategoryChangedV1>(ea.Body.Span, JsonOptions)
                    ?? throw new JsonException("Empty body for wa.template.category_changed.v1");
                tenantContext.OverrideTenantId = evt.TenantId;
                processed = await dispatcher.SendAsync(new ProcessTemplateCategoryChangedCommand(evt), stoppingToken);
            }
            else
            {
                LogUnknownRoutingKey(_logger, routingKey);
            }
        }
        catch (Exception ex)
        {
            LogHandlerFailed(_logger, ex, routingKey);
            processed = false;
        }

        // Never requeue: a message that failed once will fail identically on immediate retry
        // (unresolvable tenant / unknown template / invalid transition are not transient). It
        // lands in the DLQ via the queue's x-dead-letter-exchange argument instead.
        if (processed)
            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
        else
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: stoppingToken);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Template events consumer loop failed; reconnecting in 5s")]
    private static partial void LogConsumerLoopFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unknown routing key '{RoutingKey}' delivered to wa-admin.template-events")]
    private static partial void LogUnknownRoutingKey(ILogger logger, string routingKey);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to process message with routing key '{RoutingKey}' — parking to DLQ")]
    private static partial void LogHandlerFailed(ILogger logger, Exception exception, string routingKey);
}
