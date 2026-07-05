using System.Text;
using System.Text.Json;
using WaGateway.Application.Campaigns.Logic;
using WaGateway.Application.Common.Interfaces;
using WaGateway.Infrastructure.Persistence;
using WaPlatform.Contracts.IntegrationEvents.V1;
using wavio.SharedDataModel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace WaGateway.Infrastructure.Messaging;

/// <summary>
/// Consumes <c>wa.message.status.v1</c> off the shared <c>wavio.events</c> topic exchange
/// (issue #13's producer) to roll up campaign progress (issue #22) — delivered/read/failed
/// recipient transitions and the campaign-level counters/completion they drive. Same durable
/// queue / manual-ack / tenant-resolution shape as WaBilling's <c>MessageStatusConsumerService</c>
/// (issue #19), which this mirrors; a dedicated queue (own name below) so this consumer's
/// ack/nack lifecycle is independent of WaBilling's.
///
/// Most <c>wa.message.status.v1</c> events are NOT for a campaign recipient (an ad hoc
/// <c>POST /v1/messages</c> send has no <c>campaign_recipients</c> row at all) — this consumer
/// looks up the recipient by <c>outbound_message_id</c> and, when none exists, acks and no-ops;
/// this is the expected common case, not an error.
/// </summary>
public sealed partial class CampaignStatusConsumerService : BackgroundService
{
    public const string QueueName = "wa-gateway.campaigns.wa.message.status.v1";
    private const string ExchangeName = "wavio.events"; // matches WaIngest/WaIntel/WaBilling's publisher constant

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RabbitMqConnectionManager _connectionManager;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CampaignStatusConsumerService> _logger;

    public CampaignStatusConsumerService(
        RabbitMqConnectionManager connectionManager, IServiceScopeFactory scopeFactory, ILogger<CampaignStatusConsumerService> logger)
    {
        _connectionManager = connectionManager;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var channel = await _connectionManager.CreateChannelAsync(stoppingToken);

        await channel.ExchangeDeclareAsync(
            exchange: ExchangeName, type: ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: stoppingToken);

        await channel.QueueDeclareAsync(
            queue: QueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);

        await channel.QueueBindAsync(
            queue: QueueName, exchange: ExchangeName, routingKey: MessageStatusV1.Name, cancellationToken: stoppingToken);

        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) => await OnMessageAsync(channel, delivery, stoppingToken);

        await channel.BasicConsumeAsync(queue: QueueName, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);

        await Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
    }

    private async Task OnMessageAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        try
        {
            var evt = JsonSerializer.Deserialize<MessageStatusV1>(Encoding.UTF8.GetString(delivery.Body.Span), JsonOptions);
            if (evt is null)
            {
                LogMalformed(_logger, null);
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var tenantResolver = scope.ServiceProvider.GetRequiredService<ITenantResolver>();

            var resolved = await tenantResolver.ResolveAsync(evt.PhoneNumberId, stoppingToken);
            if (resolved is null)
            {
                // Legitimately parked — same Wave 1 reality as WaBilling's consumer (issue #19):
                // waba.phone_numbers is empty until WABA onboarding (issue #6) provisions it.
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
                return;
            }

            if (scope.ServiceProvider.GetRequiredService<ICurrentTenant>() is ScopedCurrentTenant scopedTenant)
            {
                scopedTenant.OverrideTenantId = resolved.TenantId;
            }

            var db = scope.ServiceProvider.GetRequiredService<IWaGatewayDbContext>();
            await ApplyStatusAsync(db, resolved.TenantId, evt, stoppingToken);

            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogProcessingFailed(_logger, ex);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, stoppingToken);
        }
    }

    /// <summary>Idempotent by construction: a redelivered/duplicate event either finds the
    /// recipient already at (or past) the target status — <see cref="CampaignRecipientStatusRules.IsForwardTransition"/>
    /// rejects the no-op regression — or the fenced <c>WHERE version = @expected</c>-free
    /// conditional update below simply reapplies the same terminal write again harmlessly
    /// (failed→failed is a no-op via <see cref="CampaignRecipientStatusRules.CanTransitionToFailed"/>'s
    /// own current-state guard).</summary>
    private static async Task ApplyStatusAsync(IWaGatewayDbContext db, Guid tenantId, MessageStatusV1 evt, CancellationToken ct)
    {
        var message = await db.OutboundMessages.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.Wamid == evt.Wamid)
            .Select(m => new { m.Id })
            .FirstOrDefaultAsync(ct);
        if (message is null)
        {
            return; // no matching send at all yet (webhook raced the dispatcher's own write) — a redelivery will catch it
        }

        var recipient = await db.CampaignRecipients
            .Where(r => r.OutboundMessageId == message.Id)
            .Select(r => new { r.Id, r.Status, r.CampaignId })
            .FirstOrDefaultAsync(ct);
        if (recipient is null)
        {
            return; // not a campaign send — the common case, not an error
        }

        var now = DateTimeOffset.UtcNow;
        string? newStatus = evt.Status switch
        {
            "delivered" or "read" when CampaignRecipientStatusRules.IsForwardTransition(recipient.Status, evt.Status) => evt.Status,
            "failed" when CampaignRecipientStatusRules.CanTransitionToFailed(recipient.Status) => CampaignRecipientStatusRules.Failed,
            _ => null, // "sent"/"deleted", or a no-op/out-of-order transition — nothing to apply
        };
        if (newStatus is null)
        {
            return;
        }

        var rowsAffected = await db.CampaignRecipients
            .Where(r => r.Id == recipient.Id && r.Status == recipient.Status)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, newStatus)
                .SetProperty(r => r.ErrorCode, newStatus == CampaignRecipientStatusRules.Failed
                    ? evt.ErrorCode?.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : null)
                .SetProperty(r => r.ProcessedAt, now)
                .SetProperty(r => r.UpdatedAt, now)
                .SetProperty(r => r.Version, r => r.Version + 1), ct);
        if (rowsAffected == 0)
        {
            return; // lost the race to a concurrent redelivery — the winner already counted it
        }

        switch (newStatus)
        {
            case "delivered":
                await db.Campaigns.Where(c => c.Id == recipient.CampaignId)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.DeliveredCount, c => c.DeliveredCount + 1).SetProperty(c => c.UpdatedAt, now), ct);
                break;
            case "read":
                await db.Campaigns.Where(c => c.Id == recipient.CampaignId)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.ReadCount, c => c.ReadCount + 1).SetProperty(c => c.UpdatedAt, now), ct);
                break;
            case "failed":
                await db.Campaigns.Where(c => c.Id == recipient.CampaignId)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.FailedCount, c => c.FailedCount + 1).SetProperty(c => c.UpdatedAt, now), ct);
                break;
        }

        var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == recipient.CampaignId, ct);
        if (campaign is not null)
        {
            var pendingOrSent = await db.CampaignRecipients.CountAsync(
                r => r.CampaignId == campaign.Id && (r.Status == "pending" || r.Status == "sent"), ct);
            if (campaign.Status == "running" && CampaignRecipientStatusRules.IsCampaignComplete(pendingOrSent))
            {
                await db.Campaigns.Where(c => c.Id == campaign.Id && c.Status == "running")
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(c => c.Status, "completed")
                        .SetProperty(c => c.CompletedAt, now)
                        .SetProperty(c => c.UpdatedAt, now)
                        .SetProperty(c => c.Version, c => c.Version + 1), ct);
            }
        }
    }

    private static readonly Action<ILogger, Exception?> LogMalformed = LoggerMessage.Define(
        LogLevel.Warning, new EventId(1, nameof(LogMalformed)),
        "Received a wa.message.status.v1 delivery that failed to deserialize — acking to drop it (malformed, not retryable)");

    private static readonly Action<ILogger, Exception> LogProcessingFailed = LoggerMessage.Define(
        LogLevel.Error, new EventId(2, nameof(LogProcessingFailed)),
        "Failed to process a wa.message.status.v1 delivery for campaign rollup — requeuing");
}
