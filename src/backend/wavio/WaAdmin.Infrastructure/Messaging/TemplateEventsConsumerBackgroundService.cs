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
/// One DI scope PER ATTEMPT (not per message — see <see cref="ProcessOnceAsync"/>):
/// <see cref="ScopedCurrentTenant"/> is set to the event's TenantId before the scope's
/// <see cref="IDispatcher"/> touches any tenant-scoped table, so RLS applies correctly even
/// though there is no HTTP request driving this scope (see <see cref="ScopedCurrentTenant"/>'s
/// doc comment). A fresh scope (and therefore a fresh <c>DbContext</c>/connection) per retry
/// attempt avoids reusing a connection object that may itself be in a faulted state after a
/// transient DB failure.
///
/// Manual ack, via <see cref="TransientRetryPolicy"/> (issue #16 security-review follow-up, S1):
/// a <c>false</c> return ("parked" — unresolvable tenant, unknown template, invalid transition)
/// or a non-transient exception (malformed payload) is Nacked WITHOUT requeue, landing in
/// <see cref="DeadLetterQueueName"/> via the queue's <c>x-dead-letter-exchange</c> argument —
/// retrying either would never succeed. A TRANSIENT exception (DB/network — see
/// <see cref="TransientRetryPolicy.IsTransient"/>) gets a few in-process retries with backoff
/// first; if it still hasn't recovered, the message is Nacked WITH requeue (never dead-lettered)
/// so a brief Postgres/network outage cannot permanently lose a legitimate Meta status
/// transition — it just waits for the dependency to recover and a later redelivery.
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
        // Defensive copy: the retry loop below may re-read this body across several attempts
        // spread over multiple seconds, after the original delivery's buffer could in principle
        // be reused by the client library.
        var bodyBytes = ea.Body.ToArray();

        MessageProcessingOutcome outcome;
        try
        {
            outcome = await TransientRetryPolicy.ExecuteAsync(
                () => ProcessOnceAsync(routingKey, bodyBytes, stoppingToken),
                Task.Delay,
                _logger,
                stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Host shutting down mid-retry-delay — leave the delivery unacked; RabbitMQ redelivers
            // it once a consumer (this one, after restart, or another replica) reconnects.
            return;
        }

        switch (outcome)
        {
            case MessageProcessingOutcome.Processed:
                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
                break;
            case MessageProcessingOutcome.Requeue:
                LogRequeued(_logger, routingKey);
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true, cancellationToken: stoppingToken);
                break;
            case MessageProcessingOutcome.DeadLetter:
            default:
                LogDeadLettered(_logger, routingKey);
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: stoppingToken);
                break;
        }
    }

    /// <summary>One attempt at processing a message. Each call gets its own DI scope (and
    /// therefore a fresh <c>DbContext</c>) — see the type's doc comment for why that matters for
    /// retries specifically.</summary>
    private async Task<bool> ProcessOnceAsync(string routingKey, byte[] body, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ScopedCurrentTenant>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        if (routingKey == TemplateStatusChangedV1.Name)
        {
            var evt = JsonSerializer.Deserialize<TemplateStatusChangedV1>(body, JsonOptions)
                ?? throw new JsonException("Empty body for wa.template.status_changed.v1");
            tenantContext.OverrideTenantId = evt.TenantId;
            return await dispatcher.SendAsync(new ProcessTemplateStatusChangedCommand(evt), cancellationToken);
        }

        if (routingKey == TemplateCategoryChangedV1.Name)
        {
            var evt = JsonSerializer.Deserialize<TemplateCategoryChangedV1>(body, JsonOptions)
                ?? throw new JsonException("Empty body for wa.template.category_changed.v1");
            tenantContext.OverrideTenantId = evt.TenantId;
            return await dispatcher.SendAsync(new ProcessTemplateCategoryChangedCommand(evt), cancellationToken);
        }

        // Unknown routing key bound to this queue somehow — park it rather than lose it. Not an
        // exception: retrying would never resolve a routing mismatch.
        LogUnknownRoutingKey(_logger, routingKey);
        return false;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Template events consumer loop failed; reconnecting in 5s")]
    private static partial void LogConsumerLoopFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unknown routing key '{RoutingKey}' delivered to wa-admin.template-events")]
    private static partial void LogUnknownRoutingKey(ILogger logger, string routingKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Requeueing message with routing key '{RoutingKey}' after exhausting in-process retries (transient failure)")]
    private static partial void LogRequeued(ILogger logger, string routingKey);

    [LoggerMessage(Level = LogLevel.Error, Message = "Dead-lettering message with routing key '{RoutingKey}' (permanent failure or parked business outcome)")]
    private static partial void LogDeadLettered(ILogger logger, string routingKey);
}
