using System.Text;
using System.Text.Json;
using WaBilling.Application.Common.Interfaces;
using WaBilling.Application.Costs.Commands.RecordMessageCost;
using WaPlatform.Contracts.IntegrationEvents.V1;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace WaBilling.Infrastructure.Messaging;

/// <summary>
/// Consumes <c>wa.message.status.v1</c> off the shared <c>wavio.events</c> topic exchange
/// (issue #13's producer; issue #19's consumer) into the PMP cost ledger. Durable queue, manual
/// ack — a message is only acked once the ledger insert has actually committed (or the event is
/// legitimately parked), so a crash mid-processing redelivers rather than silently dropping a
/// billing event.
///
/// Tenant resolution (<see cref="ITenantResolver"/>) happens here, per-message, BEFORE
/// dispatching the command — same convention as WaIntel's <c>MessageReceivedConsumerService</c>
/// (issue #15). Wave 1 reality: resolution fails for every event today (waba.phone_numbers is
/// empty, issue #6 onboarding doesn't exist yet). An unresolvable event is ACKED (not requeued)
/// with a logged warning — never written under a placeholder tenant.
/// </summary>
public sealed class MessageStatusConsumerService : BackgroundService
{
    public const string QueueName = "wa-billing.wa.message.status.v1";
    private const string ExchangeName = "wavio.events"; // matches WaIngest/WaIntel's publisher constant

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RabbitMqConnectionManager _connectionManager;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MessageStatusConsumerService> _logger;

    public MessageStatusConsumerService(
        RabbitMqConnectionManager connectionManager,
        IServiceScopeFactory scopeFactory,
        ILogger<MessageStatusConsumerService> logger)
    {
        _connectionManager = connectionManager;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var channel = await _connectionManager.CreateChannelAsync(stoppingToken);

        await channel.ExchangeDeclareAsync(
            exchange: ExchangeName,
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
            exchange: ExchangeName,
            routingKey: MessageStatusV1.Name,
            cancellationToken: stoppingToken);

        // One in-flight message at a time — a ledger insert is cheap, and this keeps the
        // check-then-insert idempotency guard (RecordMessageCostCommandHandler) free of
        // same-consumer concurrent-write races.
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
            var evt = JsonSerializer.Deserialize<MessageStatusV1>(
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
                // Legitimately parked (see class doc comment) — ack so the queue doesn't spin on
                // an unresolvable lookup.
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
                return;
            }

            await dispatcher.SendAsync(
                new RecordMessageCostCommand(
                    resolved.TenantId,
                    resolved.PhoneNumberId,
                    evt.Wamid,
                    evt.PricingCategory,
                    evt.PricingModel,
                    evt.Billable,
                    evt.Amount,
                    evt.Currency,
                    evt.DestinationMarket,
                    evt.PricingRawJson),
                stoppingToken);

            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A real failure (DB down, etc.) — requeue for retry rather than lose the billing event.
            LogProcessingFailed(_logger, ex);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, stoppingToken);
        }
    }

    private static readonly Action<ILogger, Exception?> LogMalformed = LoggerMessage.Define(
        LogLevel.Warning, new EventId(1, nameof(LogMalformed)),
        "Received a wa.message.status.v1 delivery that failed to deserialize — acking to drop it (malformed, not retryable)");

    private static readonly Action<ILogger, Exception> LogProcessingFailed = LoggerMessage.Define(
        LogLevel.Error, new EventId(2, nameof(LogProcessingFailed)),
        "Failed to process a wa.message.status.v1 delivery — requeuing");
}
