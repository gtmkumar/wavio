using System.Text;
using System.Text.Json;
using WaIntel.Application.Common.Interfaces;
using WaIntel.Application.Windows.Commands.UpsertWindowOnMessageReceived;
using WaPlatform.Contracts.IntegrationEvents.V1;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace WaIntel.Infrastructure.Messaging;

/// <summary>
/// Consumes <c>wa.message.received.v1</c> off the shared <c>wavio.events</c> topic exchange
/// (issue #13's producer, issue #15's first consumer) and upserts the corresponding
/// conversation window. Durable queue, manual ack — a message is only acked once the window
/// upsert has actually committed (or the event is legitimately parked; see below), so a crash
/// mid-processing redelivers rather than silently losing the update.
///
/// Tenant resolution (<see cref="ITenantResolver"/>) happens here, per-message, BEFORE
/// dispatching the command — the Application layer's handler never sees an unresolved event.
/// Wave 1 reality: resolution fails for every event today (waba.phone_numbers is empty, issue #6
/// onboarding doesn't exist yet). An unresolvable event is ACKED (not requeued) with a logged
/// warning — NOT written as a Guid.Empty-tenant row (would violate the FK, and more importantly
/// would be an orphaned row invisible to every RLS-scoped read). Requeuing would just spin
/// forever on the same unresolvable lookup; wa-ingest-svc's raw_webhooks is the durable source of
/// truth this could be replayed from once onboarding ships, not this consumer's queue.
/// </summary>
public sealed class MessageReceivedConsumerService : BackgroundService
{
    public const string QueueName = "wa-intel.wa.message.received.v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RabbitMqConnectionManager _connectionManager;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MessageReceivedConsumerService> _logger;

    public MessageReceivedConsumerService(
        RabbitMqConnectionManager connectionManager,
        IServiceScopeFactory scopeFactory,
        ILogger<MessageReceivedConsumerService> logger)
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
            routingKey: MessageReceivedV1.Name,
            cancellationToken: stoppingToken);

        // One in-flight message at a time per consumer — window upserts are cheap, and this keeps
        // ordering-per-customer roughly intact without needing a partitioned/sharded consumer.
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) => await OnMessageAsync(channel, delivery, stoppingToken);

        await channel.BasicConsumeAsync(
            queue: QueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        // Keep the service alive for the lifetime of the host; the consumer callback above does
        // the actual work as deliveries arrive.
        await Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
    }

    private async Task OnMessageAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        try
        {
            var evt = JsonSerializer.Deserialize<MessageReceivedV1>(
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
                // Legitimately parked, not an error — ack so the queue doesn't spin on an
                // unresolvable lookup (see class doc comment).
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
                return;
            }

            await dispatcher.SendAsync(
                new UpsertWindowOnMessageReceivedCommand(
                    resolved.TenantId,
                    resolved.PhoneNumberId,
                    evt.WaId,
                    evt.SentAt,
                    evt.OpensCustomerServiceWindow,
                    evt.Referral),
                stoppingToken);

            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A real failure (DB down, etc.) — requeue for retry rather than lose the update.
            LogProcessingFailed(_logger, ex);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, stoppingToken);
        }
    }

    private static readonly Action<ILogger, Exception?> LogMalformed = LoggerMessage.Define(
        LogLevel.Warning, new EventId(1, nameof(LogMalformed)),
        "Received a wa.message.received.v1 delivery that failed to deserialize — acking to drop it (malformed, not retryable)");

    private static readonly Action<ILogger, Exception> LogProcessingFailed = LoggerMessage.Define(
        LogLevel.Error, new EventId(2, nameof(LogProcessingFailed)),
        "Failed to process a wa.message.received.v1 delivery — requeuing");
}
