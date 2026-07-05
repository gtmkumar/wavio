using System.Text;
using System.Text.Json;
using WaIntel.Application.Common.Interfaces;
using WaIntel.Application.Quality.Commands.RecordQualityChange;
using WaPlatform.Contracts.IntegrationEvents.V1;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace WaIntel.Infrastructure.Messaging;

/// <summary>
/// Consumes <c>wa.quality.changed.v1</c> off the shared <c>wavio.events</c> topic exchange
/// (WaIngest's <c>MetaWebhookNormalizer</c> publishes it; this is its first consumer — issue #20,
/// spec §4.6). Same durable-queue/manual-ack/tenant-resolve-then-dispatch skeleton as
/// <c>MessageReceivedConsumerService</c> (issue #15) — see that class's doc comment for the full
/// rationale (unresolvable tenant is parked via ack, not requeued; real failures nack+requeue).
/// </summary>
public sealed class QualityChangedConsumerService : BackgroundService
{
    public const string QueueName = "wa-intel.wa.quality.changed.v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RabbitMqConnectionManager _connectionManager;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<QualityChangedConsumerService> _logger;

    public QualityChangedConsumerService(
        RabbitMqConnectionManager connectionManager,
        IServiceScopeFactory scopeFactory,
        ILogger<QualityChangedConsumerService> logger)
    {
        _connectionManager = connectionManager;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var channel = await _connectionManager.CreateChannelAsync(stoppingToken);

        await channel.ExchangeDeclareAsync(
            exchange: RabbitMqEventBusPublisher.ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: stoppingToken);

        await channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken);

        await channel.QueueBindAsync(
            queue: QueueName,
            exchange: RabbitMqEventBusPublisher.ExchangeName,
            routingKey: QualityChangedV1.Name,
            cancellationToken: stoppingToken);

        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) => await OnMessageAsync(channel, delivery, stoppingToken);

        await channel.BasicConsumeAsync(
            queue: QueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        await Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
    }

    private async Task OnMessageAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        try
        {
            var evt = JsonSerializer.Deserialize<QualityChangedV1>(
                Encoding.UTF8.GetString(delivery.Body.Span), JsonOptions);

            if (evt is null)
            {
                LogMalformed(_logger, null);
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var tenantResolver = scope.ServiceProvider.GetRequiredService<ITenantResolver>();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

            var resolved = await tenantResolver.ResolveAsync(evt.PhoneNumberId, stoppingToken);
            if (resolved is null)
            {
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
                return;
            }

            await dispatcher.SendAsync(
                new RecordQualityChangeCommand(
                    resolved.TenantId,
                    resolved.PhoneNumberId,
                    evt.WabaId,
                    evt.CurrentRating,
                    EventSource: "webhook",
                    RawPayload: null),
                stoppingToken);

            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogProcessingFailed(_logger, ex);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, stoppingToken);
        }
    }

    private static readonly Action<ILogger, Exception?> LogMalformed = LoggerMessage.Define(
        LogLevel.Warning, new EventId(1, nameof(LogMalformed)),
        "Received a wa.quality.changed.v1 delivery that failed to deserialize — acking to drop it (malformed, not retryable)");

    private static readonly Action<ILogger, Exception> LogProcessingFailed = LoggerMessage.Define(
        LogLevel.Error, new EventId(2, nameof(LogProcessingFailed)),
        "Failed to process a wa.quality.changed.v1 delivery — requeuing");
}
