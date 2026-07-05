using System.Text.Json;
using WaGateway.Application.Common.Interfaces;
using WaGateway.Application.Messages.Dtos;
using WaGateway.Application.Messages.Logic;
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
///
/// DUPLICATE-SEND HAZARD, found live (security review, PR #45, S1) — a live-in-flight slow Graph
/// response (not a crash) could ALSO trigger the reclaim: the reclaim condition only looks at
/// <c>locked_at</c> age, so a call slower than <c>Outbox:StaleLockSeconds</c> gets its lease
/// stolen while still running, the reclaimer re-sends, and — since every completion write used to
/// be an unconditional tracked-entity <c>SaveChangesAsync</c> — whichever call finished last would
/// silently overwrite the other's result. Fixed two ways, together:
///   1. <see cref="Graph.MetaGraphMessageClient"/>'s HttpClient now has an explicit
///      <c>Timeout</c> strictly less than <c>Outbox:StaleLockSeconds</c> (enforced by a startup
///      sanity check in <c>DependencyInjection.cs</c>), so a slow call fails fast — classified
///      transient, retried through the normal backoff path — well before the reclaim window
///      could ever open while it's still running. This closes the exposure at the source.
///   2. Every write below that transitions the entry's status is fenced with a conditional
///      <c>ExecuteUpdateAsync(... WHERE locked_by = <see cref="_instanceId"/> AND status =
///      'dispatching')</c>; 0 rows affected means the lease was already lost, and the caller
///      discards its own result rather than writing anything (including the corresponding
///      <c>outbound_messages</c> row). This is the second, independent guard for the residual
///      window between the Graph call returning and the write landing, and for genuine crash
///      recovery races.
/// </summary>
public sealed partial class OutboxDispatcherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMetaGraphMessageClient _graphClient;
    private readonly TokenBucketRateLimiter _tokenBucket;
    private readonly MessagingTierGate _tierGate;
    private readonly GuardianThrottleGate _guardianGate;
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
        GuardianThrottleGate guardianGate,
        IOptions<MetaGraphOptions> graphOptions,
        IConfiguration configuration,
        ILogger<OutboxDispatcherService> logger)
    {
        _scopeFactory = scopeFactory;
        _graphClient = graphClient;
        _tokenBucket = tokenBucket;
        _tierGate = tierGate;
        _guardianGate = guardianGate;
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

    /// <summary>Test seam (issue #46): the lease id this instance claims outbox entries under.
    /// <c>ExecuteUpdateAsync</c> (the fenced-write mechanism this whole class hinges on) throws
    /// against EF Core's InMemory provider, so this class has zero unit-test coverage (see the
    /// class doc comment's duplicate-send-hazard section and
    /// .claude/agent-memory/dotnet-backend-developer/issue-46-integration-tests.md) — real
    /// coverage requires driving <see cref="LeaseNextBatchAsync"/>/<see cref="ProcessEntryAsync"/>
    /// directly against a real Postgres, which needs this and those two methods visible to
    /// WaPlatform.IntegrationTests (<c>InternalsVisibleTo</c> in this project's .csproj).</summary>
    internal string InstanceId => _instanceId;

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
    internal async Task<List<Guid>> LeaseNextBatchAsync(CancellationToken ct)
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

    internal async Task ProcessEntryAsync(Guid outboxEntryId, CancellationToken ct)
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
            await FencedMarkDeadAsync(db, entry.Id, "ORPHANED", "No matching outbound_messages row.", ct);
            return;
        }

        var phoneNumber = await db.WabaPhoneNumbers.FirstOrDefaultAsync(p => p.Id == entry.PhoneNumberId, ct);
        if (phoneNumber is null)
        {
            LogUnresolvedPhoneNumber(_logger, entry.PhoneNumberId);
            if (await FencedMarkDeadAsync(db, entry.Id, "UNRESOLVED_PHONE_NUMBER", "No matching waba.phone_numbers row.", ct))
            {
                await UpdateMessageFailedAsync(db, message.Id, "UNRESOLVED_PHONE_NUMBER", "No matching waba.phone_numbers row.", ct);
                // No Meta id to report — that IS the failure. Falls back to the internal GUID's
                // string form; every other publish call site below uses the real Meta id.
                await PublishFailureAsync(scope, entry, message, entry.PhoneNumberId.ToString(), "UNRESOLVED_PHONE_NUMBER", "No matching waba.phone_numbers row.", ct);
            }
            return;
        }

        // Guardian auto-throttle (issue #20, spec §4.6) — checked directly against
        // quality.guardian_incidents on this same tenant-scoped scope (see IWaGatewayDbContext's
        // doc comment for why this reads the DB directly rather than a pg_notify-backed cache).
        // Only ever applies to marketing sends; utility/authentication/service always proceed.
        if (IsMarketingTemplate(message))
        {
            var throttleAction = await db.GuardianIncidents
                .AsNoTracking()
                .Where(i => i.PhoneNumberId == entry.PhoneNumberId && i.Status != "resolved")
                .OrderByDescending(i => i.OpenedAt)
                .Select(i => i.ThrottleAction)
                .FirstOrDefaultAsync(ct);

            if (GuardianThrottleRules.IsFrozen(throttleAction))
            {
                // Not a failure — a RED-quality freeze is meant to be temporary (spec §4.6:
                // "recovery to GREEN -> resolve open incidents"). Release for retry rather than
                // dead-letter, same "throttling is not a failure" treatment as the token bucket
                // below; a prolonged freeze still eventually dead-letters via MaxAttempts exhaustion.
                await FencedReleaseForRetryAsync(db, entry.Id, TimeSpan.FromMinutes(1), ct);
                return;
            }

            if (GuardianThrottleRules.IsHalved(throttleAction) && !_guardianGate.TryAllowHalvedSend(entry.PhoneNumberId))
            {
                await FencedReleaseForRetryAsync(db, entry.Id, TimeSpan.FromSeconds(2), ct);
                return;
            }
        }

        if (!_tokenBucket.TryConsume(entry.PhoneNumberId, _graphOptions.DefaultThroughputPerSecond))
        {
            // Throttling is not a failure — release the lease and try again shortly without
            // spending one of the message's retry attempts. Fenced like every other write below
            // (security review, PR #45, S1) even though the pre-Graph-call window makes a lost
            // race here vanishingly unlikely — uniform defense is cheaper to reason about than
            // selectively skipping the "safe" cases.
            await FencedReleaseForRetryAsync(db, entry.Id, TimeSpan.FromSeconds(1), ct);
            return;
        }

        if (IsMarketingTemplate(message) &&
            !_tierGate.TryRegister(entry.PhoneNumberId, message.ToWaId, _graphOptions.DefaultMessagingTierPerDay))
        {
            if (!await FencedMarkDeadAsync(db, entry.Id, "TIER_EXHAUSTED", "Messaging-tier headroom exhausted for this phone number.", ct))
            {
                return; // lease already lost — whoever holds it now owns the outcome
            }
            await UpdateMessageFailedAsync(db, message.Id, "TIER_EXHAUSTED", "Messaging-tier headroom exhausted for this phone number.", ct);
            await PublishFailureAsync(scope, entry, message, phoneNumber.MetaPhoneNumberId, "TIER_EXHAUSTED", "Messaging-tier headroom exhausted for this phone number.", ct);
            return;
        }

        // Durable checkpoint BEFORE calling Graph: attempts (and therefore MaxAttempts) are
        // correct even if the process crashes between this write and the Graph response — see
        // the class doc comment on the exactly-once limitation. Fenced: if we've somehow already
        // lost the lease before even calling Graph, don't call it at all.
        var attemptsRecorded = await db.OutboundOutboxEntries
            .Where(e => e.Id == entry.Id && e.LockedBy == _instanceId && e.Status == "dispatching")
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.Attempts, e => e.Attempts + 1)
                .SetProperty(e => e.UpdatedAt, DateTimeOffset.UtcNow), ct);
        if (attemptsRecorded == 0)
        {
            LogLeaseLost(_logger, entry.Id, "pre-dispatch attempts checkpoint");
            return;
        }
        var attemptNumber = entry.Attempts + 1;

        // Timeout strictly < Outbox:StaleLockSeconds (DependencyInjection.cs's startup sanity
        // check enforces this) — a slow Graph response fails fast here (classified transient by
        // MetaGraphMessageClient) well before the lease could go stale and be reclaimed while
        // still in flight (security review, PR #45, S1).
        var graphResult = await _graphClient.SendAsync(
            new GraphSendRequest(phoneNumber.MetaPhoneNumberId, message.ToWaId, message.MessageType, message.Payload), ct);

        if (graphResult.Success)
        {
            var now = DateTimeOffset.UtcNow;

            // Fence FIRST, before touching outbound_messages: this atomic, conditional UPDATE is
            // the actual race gate. If it affects 0 rows, another instance/tick already reclaimed
            // this entry (and is — or already did — record its own outcome) — discard our result
            // entirely rather than risk overwriting a fresher write with a stale one.
            var claimed = await db.OutboundOutboxEntries
                .Where(e => e.Id == entry.Id && e.LockedBy == _instanceId && e.Status == "dispatching")
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(e => e.Status, "dispatched")
                    .SetProperty(e => e.UpdatedAt, now), ct);

            if (claimed == 0)
            {
                LogLeaseLost(_logger, entry.Id, "post-success completion");
                return;
            }

            await db.OutboundMessages
                .Where(m => m.Id == message.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(m => m.Status, "dispatched")
                    .SetProperty(m => m.Wamid, graphResult.Wamid)
                    .SetProperty(m => m.DispatchedAt, now)
                    .SetProperty(m => m.UpdatedAt, now)
                    .SetProperty(m => m.Version, m => m.Version + 1), ct);
            return;
        }

        if (graphResult.IsTransientFailure && attemptNumber < entry.MaxAttempts)
        {
            var backoff = ComputeBackoffWithJitter(attemptNumber);
            await db.OutboundOutboxEntries
                .Where(e => e.Id == entry.Id && e.LockedBy == _instanceId && e.Status == "dispatching")
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(e => e.Status, "failed")
                    .SetProperty(e => e.NextAttemptAt, DateTimeOffset.UtcNow + backoff)
                    .SetProperty(e => e.LastErrorCode, graphResult.ErrorCode)
                    .SetProperty(e => e.LastError, graphResult.ErrorMessage)
                    .SetProperty(e => e.LockedBy, (string?)null)
                    .SetProperty(e => e.LockedAt, (DateTimeOffset?)null)
                    .SetProperty(e => e.UpdatedAt, DateTimeOffset.UtcNow), ct);
            // No message-row write on the retry path (status stays "accepted") — nothing to fence
            // here beyond the entry transition itself; a lost race just means the reclaimer's
            // own attempt/backoff bookkeeping wins instead, which is equally correct.
            return;
        }

        // Either a permanent Graph error, or a transient one that exhausted its retries.
        var errorCode = graphResult.IsTransientFailure ? "RETRIES_EXHAUSTED" : graphResult.ErrorCode ?? "UNKNOWN";
        var errorMessage = graphResult.ErrorMessage ?? "Send failed.";
        if (!await FencedMarkDeadAsync(db, entry.Id, errorCode, errorMessage, ct))
        {
            LogLeaseLost(_logger, entry.Id, "post-failure dead-letter");
            return;
        }
        await UpdateMessageFailedAsync(db, message.Id, errorCode, errorMessage, ct);
        await PublishFailureAsync(scope, entry, message, phoneNumber.MetaPhoneNumberId, errorCode, errorMessage, ct);
    }

    /// <summary>Atomically transitions the entry to 'dead' only if this instance still holds the
    /// lease. Returns false if the lease was already lost — caller must not write anything else.</summary>
    private async Task<bool> FencedMarkDeadAsync(
        IWaGatewayDbContext db, Guid entryId, string errorCode, string errorMessage, CancellationToken ct)
    {
        var rowsAffected = await db.OutboundOutboxEntries
            .Where(e => e.Id == entryId && e.LockedBy == _instanceId && e.Status == "dispatching")
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.Status, "dead")
                .SetProperty(e => e.LastErrorCode, errorCode)
                .SetProperty(e => e.LastError, errorMessage)
                .SetProperty(e => e.UpdatedAt, DateTimeOffset.UtcNow), ct);
        return rowsAffected > 0;
    }

    private static Task<int> UpdateMessageFailedAsync(
        IWaGatewayDbContext db, Guid messageId, string errorCode, string errorMessage, CancellationToken ct) =>
        db.OutboundMessages
            .Where(m => m.Id == messageId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(m => m.Status, "failed")
                .SetProperty(m => m.ErrorCode, errorCode)
                .SetProperty(m => m.ErrorMessage, errorMessage)
                .SetProperty(m => m.UpdatedAt, DateTimeOffset.UtcNow)
                .SetProperty(m => m.Version, m => m.Version + 1), ct);

    private Task<int> FencedReleaseForRetryAsync(IWaGatewayDbContext db, Guid entryId, TimeSpan delay, CancellationToken ct) =>
        db.OutboundOutboxEntries
            .Where(e => e.Id == entryId && e.LockedBy == _instanceId && e.Status == "dispatching")
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.Status, "pending")
                .SetProperty(e => e.NextAttemptAt, DateTimeOffset.UtcNow + delay)
                .SetProperty(e => e.LockedBy, (string?)null)
                .SetProperty(e => e.LockedAt, (DateTimeOffset?)null)
                .SetProperty(e => e.UpdatedAt, DateTimeOffset.UtcNow), ct);

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Lost the lease on outbox entry {OutboxEntryId} during {Stage} — another instance/tick already reclaimed it; discarding this result")]
    private static partial void LogLeaseLost(ILogger logger, Guid outboxEntryId, string stage);
}
