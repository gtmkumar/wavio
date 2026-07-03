using System.Text.Json;
using WaGateway.Application.Common.Interfaces;
using WaGateway.Application.Messages.Dtos;
using WaGateway.Infrastructure.Graph;
using WaGateway.Infrastructure.Persistence;
using WaGateway.Infrastructure.RateLimiting;
using WaPlatform.Contracts.IntegrationEvents.V1;
using wavio.SharedDataModel.Contracts;
using wavio.SharedDataModel.Entities.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WaGateway.Infrastructure.BackgroundWork;

/// <summary>
/// Drains <c>messaging.outbound_outbox</c> (spec §4.2 outbox pattern, issue #14) → Meta Graph API
/// → status reconciled on <c>outbound_messages</c>. Leases entries via <c>locked_by</c>/
/// <c>locked_at</c> (the database-architect's handoff), reclaiming stale leases so a crashed
/// instance's in-flight work is picked up again rather than lost — this is what the "zero
/// message loss under crash" acceptance test exercises.
///
/// HONEST LIMITATION on exactly-once delivery: Meta's Cloud API messages endpoint has no
/// client-supplied idempotency key, so there is an unavoidable window — between the Graph HTTP
/// call succeeding and this dispatcher's own DB write recording that — where a crash can cause a
/// duplicate send on reclaim. This is a fundamental constraint of the Graph API, not a gap in
/// this implementation: what IS guaranteed is zero LOST messages (every entry is retried until
/// dispatched or permanently dead-lettered) and a bounded, documented duplicate-send window,
/// minimized by (a) durably recording the attempt BEFORE calling Graph, so attempts are counted
/// correctly across a crash, and (b) writing the Graph result to the DB immediately after the
/// HTTP call returns, with no other work in between.
/// </summary>
public sealed partial class OutboxDispatcherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMetaGraphMessageClient _graphClient;
    private readonly TokenBucketRateLimiter _tokenBucket;
    private readonly MessagingTierGate _tierGate;
    private readonly MetaGraphOptions _graphOptions;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _staleLockTimeout;
    private readonly int _batchSize;
    private readonly string _instanceId;
    private readonly ILogger<OutboxDispatcherService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public OutboxDispatcherService(
        IServiceScopeFactory scopeFactory,
        IMetaGraphMessageClient graphClient,
        TokenBucketRateLimiter tokenBucket,
        MessagingTierGate tierGate,
        IOptions<MetaGraphOptions> graphOptions,
        IConfiguration configuration,
        ILogger<OutboxDispatcherService> logger)
    {
        _scopeFactory = scopeFactory;
        _graphClient = graphClient;
        _tokenBucket = tokenBucket;
        _tierGate = tierGate;
        _graphOptions = graphOptions.Value;
        _pollInterval = TimeSpan.FromMilliseconds(configuration.GetValue("Outbox:PollIntervalMs", 1000));
        _staleLockTimeout = TimeSpan.FromSeconds(configuration.GetValue("Outbox:StaleLockSeconds", 30));
        _batchSize = configuration.GetValue("Outbox:BatchSize", 20);
        // locked_by is varchar(100) (db/migrations/V007) — cap safely rather than with a fixed
        // range index, which throws if the natural string is shorter (found live: it usually is).
        var rawInstanceId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
        _instanceId = rawInstanceId[..Math.Min(100, rawInstanceId.Length)];
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_pollInterval);
        do
        {
            try
            {
                await RunOneTickAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogTickFailed(_logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOneTickAsync(CancellationToken stoppingToken)
    {
        var claimedIds = await LeaseNextBatchAsync(stoppingToken);
        foreach (var id in claimedIds)
        {
            await ProcessEntryAsync(id, stoppingToken);
        }
    }

    /// <summary>
    /// outbound_outbox is NOT RLS-scoped (db/migrations/V007's table comment) — this scan and
    /// claim needs no tenant context at all. Reclaims stale leases (locked_at older than
    /// <see cref="_staleLockTimeout"/>) so a crashed instance's claimed-but-unfinished work is
    /// picked up again — this is the crash-recovery half of "zero message loss."
    /// </summary>
    private async Task<List<Guid>> LeaseNextBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IWaGatewayDbContext>();

        var now = DateTimeOffset.UtcNow;
        var staleCutoff = now - _staleLockTimeout;

        var candidateIds = await db.OutboundOutboxEntries
            .Where(e => (e.Status == "pending" || e.Status == "failed" || e.Status == "dispatching")
                     && e.NextAttemptAt <= now
                     && (e.LockedAt == null || e.LockedAt < staleCutoff))
            .OrderBy(e => e.NextAttemptAt)
            .Take(_batchSize)
            .Select(e => e.Id)
            .ToListAsync(ct);

        var claimed = new List<Guid>(candidateIds.Count);
        foreach (var id in candidateIds)
        {
            // Atomic re-check-and-claim: another instance (or a previous tick) may have grabbed
            // this row between the SELECT above and here.
            var rowsAffected = await db.OutboundOutboxEntries
                .Where(e => e.Id == id
                         && (e.Status == "pending" || e.Status == "failed" || e.Status == "dispatching")
                         && (e.LockedAt == null || e.LockedAt < staleCutoff))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(e => e.Status, "dispatching")
                    .SetProperty(e => e.LockedBy, _instanceId)
                    .SetProperty(e => e.LockedAt, now)
                    .SetProperty(e => e.UpdatedAt, now), ct);

            if (rowsAffected > 0)
            {
                claimed.Add(id);
            }
        }

        return claimed;
    }

    private async Task ProcessEntryAsync(Guid outboxEntryId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IWaGatewayDbContext>();

        var entry = await db.OutboundOutboxEntries.FirstOrDefaultAsync(e => e.Id == outboxEntryId, ct);
        if (entry is null)
        {
            return; // claimed then vanished — shouldn't happen (no deletes on this table), defensive only
        }

        // outbound_messages / waba.phone_numbers ARE RLS-scoped — set the tenant override for the
        // rest of this scope (see ScopedCurrentTenant's doc comment; same fix as issue #15's
        // rls-background-service-guc-gotcha, applied here at DI-registration level instead).
        if (scope.ServiceProvider.GetRequiredService<ICurrentTenant>() is ScopedCurrentTenant scopedTenant)
        {
            scopedTenant.OverrideTenantId = entry.TenantId;
        }

        var message = await db.OutboundMessages.FirstOrDefaultAsync(m => m.Id == entry.OutboundMessageId, ct);
        if (message is null)
        {
            LogOrphanedEntry(_logger, entry.Id);
            await MarkDeadAsync(db, entry, null, "ORPHANED", "No matching outbound_messages row.", ct);
            return;
        }

        var phoneNumber = await db.WabaPhoneNumbers.FirstOrDefaultAsync(p => p.Id == entry.PhoneNumberId, ct);
        if (phoneNumber is null)
        {
            LogUnresolvedPhoneNumber(_logger, entry.PhoneNumberId);
            await MarkDeadAsync(db, entry, message, "UNRESOLVED_PHONE_NUMBER", "No matching waba.phone_numbers row.", ct);
            // No Meta id to report — that IS the failure. Falls back to the internal GUID's
            // string form; every other publish call site below uses the real Meta id.
            await PublishFailureAsync(scope, entry, message, entry.PhoneNumberId.ToString(), "UNRESOLVED_PHONE_NUMBER", "No matching waba.phone_numbers row.", ct);
            return;
        }

        if (!_tokenBucket.TryConsume(entry.PhoneNumberId, _graphOptions.DefaultThroughputPerSecond))
        {
            // Throttling is not a failure — release the lease and try again shortly without
            // spending one of the message's retry attempts.
            await ReleaseForRetryAsync(db, entry, TimeSpan.FromSeconds(1), ct);
            return;
        }

        if (IsMarketingTemplate(message) &&
            !_tierGate.TryRegister(entry.PhoneNumberId, message.ToWaId, _graphOptions.DefaultMessagingTierPerDay))
        {
            await MarkDeadAsync(db, entry, message, "TIER_EXHAUSTED", "Messaging-tier headroom exhausted for this phone number.", ct);
            await PublishFailureAsync(scope, entry, message, phoneNumber.MetaPhoneNumberId, "TIER_EXHAUSTED", "Messaging-tier headroom exhausted for this phone number.", ct);
            return;
        }

        // Durable checkpoint BEFORE calling Graph: attempts (and therefore MaxAttempts) are
        // correct even if the process crashes between this write and the Graph response — see
        // the class doc comment on the exactly-once limitation.
        entry.Attempts += 1;
        entry.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var graphResult = await _graphClient.SendAsync(
            new GraphSendRequest(phoneNumber.MetaPhoneNumberId, message.ToWaId, message.MessageType, message.Payload), ct);

        if (graphResult.Success)
        {
            var now = DateTimeOffset.UtcNow;
            message.Status = "dispatched";
            message.Wamid = graphResult.Wamid;
            message.DispatchedAt = now;
            message.UpdatedAt = now;
            message.Version += 1;
            entry.Status = "dispatched";
            entry.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            return;
        }

        if (graphResult.IsTransientFailure && entry.Attempts < entry.MaxAttempts)
        {
            var backoff = ComputeBackoffWithJitter(entry.Attempts);
            entry.Status = "failed";
            entry.NextAttemptAt = DateTimeOffset.UtcNow + backoff;
            entry.LastErrorCode = graphResult.ErrorCode;
            entry.LastError = graphResult.ErrorMessage;
            entry.LockedBy = null;
            entry.LockedAt = null;
            entry.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return;
        }

        // Either a permanent Graph error, or a transient one that exhausted its retries.
        var errorCode = graphResult.IsTransientFailure ? "RETRIES_EXHAUSTED" : graphResult.ErrorCode ?? "UNKNOWN";
        var errorMessage = graphResult.ErrorMessage ?? "Send failed.";
        await MarkDeadAsync(db, entry, message, errorCode, errorMessage, ct);
        await PublishFailureAsync(scope, entry, message, phoneNumber.MetaPhoneNumberId, errorCode, errorMessage, ct);
    }

    private static bool IsMarketingTemplate(OutboundMessage message)
    {
        if (message.MessageType != MessageTypes.Template) return false;
        try
        {
            var payload = JsonSerializer.Deserialize<TemplatePayload>(message.Payload, JsonOptions);
            return payload?.Category == "marketing";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task ReleaseForRetryAsync(
        IWaGatewayDbContext db, wavio.SharedDataModel.Entities.Messaging.OutboundOutboxEntry entry, TimeSpan delay, CancellationToken ct)
    {
        entry.Status = "pending";
        entry.NextAttemptAt = DateTimeOffset.UtcNow + delay;
        entry.LockedBy = null;
        entry.LockedAt = null;
        entry.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private static async Task MarkDeadAsync(
        IWaGatewayDbContext db,
        wavio.SharedDataModel.Entities.Messaging.OutboundOutboxEntry entry,
        OutboundMessage? message,
        string errorCode,
        string errorMessage,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        entry.Status = "dead";
        entry.LastErrorCode = errorCode;
        entry.LastError = errorMessage;
        entry.UpdatedAt = now;

        if (message is not null)
        {
            message.Status = "failed";
            message.ErrorCode = errorCode;
            message.ErrorMessage = errorMessage;
            message.UpdatedAt = now;
            message.Version += 1;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <param name="metaPhoneNumberId">Meta's raw phone_number_id string — matching every other
    /// integration event's convention (MessageReceivedV1, WindowClosingV1). Only the
    /// UNRESOLVED_PHONE_NUMBER path can't supply one (that IS the failure) and falls back to the
    /// internal GUID's string form at its call site — that's the one deliberate exception.</param>
    private static async Task PublishFailureAsync(
        IServiceScope scope,
        wavio.SharedDataModel.Entities.Messaging.OutboundOutboxEntry entry,
        OutboundMessage message,
        string metaPhoneNumberId,
        string errorCode,
        string errorMessage,
        CancellationToken ct)
    {
        var publisher = scope.ServiceProvider.GetRequiredService<IEventBusPublisher>();
        await publisher.PublishAsync(
            new MessageSendFailedV1
            {
                TenantId = entry.TenantId,
                OutboundMessageId = message.Id,
                PhoneNumberId = metaPhoneNumberId,
                ToWaId = message.ToWaId,
                MessageType = message.MessageType,
                ErrorCode = errorCode,
                ErrorMessage = errorMessage,
            },
            ct);
    }

    /// <summary>Exponential backoff with jitter, capped at 5 minutes (spec §4.2: 429/5xx retry,
    /// max 5 attempts).</summary>
    private static TimeSpan ComputeBackoffWithJitter(int attempt)
    {
        var baseSeconds = Math.Min(300, Math.Pow(2, attempt));
        var jitterSeconds = Random.Shared.NextDouble() * baseSeconds * 0.25;
        return TimeSpan.FromSeconds(baseSeconds + jitterSeconds);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox dispatcher tick failed")]
    private static partial void LogTickFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox entry {OutboxEntryId} has no matching outbound_messages row — dead-lettering")]
    private static partial void LogOrphanedEntry(ILogger logger, Guid outboxEntryId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not resolve Meta phone_number_id for internal id {PhoneNumberId} — dead-lettering")]
    private static partial void LogUnresolvedPhoneNumber(ILogger logger, Guid phoneNumberId);
}
