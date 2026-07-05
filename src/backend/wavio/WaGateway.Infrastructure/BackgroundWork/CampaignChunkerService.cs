using System.Text.Json;
using WaGateway.Application.Campaigns.Logic;
using WaGateway.Application.Common.Interfaces;
using WaGateway.Application.Messages.Commands.SendMessage;
using WaGateway.Application.Messages.Dtos;
using WaGateway.Application.Messages.Logic;
using WaGateway.Infrastructure.Persistence;
using wavio.SharedDataModel.Contracts;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace WaGateway.Infrastructure.BackgroundWork;

/// <summary>
/// Drains 'running' (and due 'scheduled') campaigns into per-recipient sends, chunked to fit the
/// phone number's marketing-tier headroom (spec §4.2: "campaign engine chunks broadcasts to fit
/// tier headroom", issue #22). Every dispatched recipient is pushed through
/// <see cref="SendMessageCommand"/> — the SAME accept path <c>POST /v1/messages</c> uses (issue
/// #14) — so suppression, the window policy, the transactional outbox, the token bucket, and the
/// Guardian throttle are never duplicated here; they run exactly once, in
/// <c>SendMessageHandler</c>/<c>OutboxDispatcherService</c>, for both an ad hoc send and a
/// campaign recipient alike.
///
/// Cross-tenant discovery (<c>messaging.campaigns</c> is strict-RLS, tenant_id NOT NULL) uses the
/// privileged Admin connection ONLY to list due campaign ids/tenant ids — same "list on Admin, do
/// the real work on a tenant-scoped connection" shape as WaAdmin's
/// <c>ErasureRequestProcessorService</c> (issue #21) and WaIntel's <c>HealthSnapshotRollupService</c>
/// (issue #20). The actual per-campaign work then runs through the normal
/// <see cref="IWaGatewayDbContext"/> (EF) with <see cref="ScopedCurrentTenant.OverrideTenantId"/>
/// set — the same RLS-override mechanism <c>OutboxDispatcherService</c> already uses — because this
/// is where <see cref="IDispatcher"/> needs to run <c>SendMessageHandler</c> against a correctly
/// tenant-scoped DbContext, not a raw ADO.NET connection.
///
/// CLAIM RACE (no lease column on campaign_recipients — schema is frozen at V013, unlike
/// outbound_outbox's locked_by/locked_at): a batch of 'pending' rows is SELECTed, then each
/// recipient's outcome is written back with a FENCED <c>WHERE status = 'pending'</c> conditional
/// update. If two chunker instances (or two overlapping ticks) select overlapping rows, calling
/// <see cref="SendMessageCommand"/> twice for the same recipient is harmless — the idempotency key
/// is deterministic per (campaign, recipient) — and only the FIRST fenced write wins, so counters
/// are never double-incremented. This is weaker than the outbox's lease but proportionate: a
/// human-triggered, low-frequency broadcast chunk, not a hot per-message dispatch loop.
///
/// TEMPLATE PAUSE/DISABLE (db/migrations/V009's own comment: "Guardian ... freeze campaigns using
/// paused template"): a PAUSED pinned template holds the campaign's chunking for this tick only
/// (same "throttling is not a failure" treatment as a Guardian quality freeze — the next tick
/// resumes once unpaused). A DISABLED pinned template is terminal in the template state machine,
/// so the campaign is failed outright and its remaining pending recipients are cancelled.
///
/// Each campaign is processed inside its own try/catch (mirrors WaAdmin's
/// <c>ErasureRequestProcessorService.ProcessOneAsync</c> — a broken campaign logs and is skipped
/// this tick rather than blocking every other due campaign in the same batch).
/// </summary>
public sealed partial class CampaignChunkerService : BackgroundService
{
    // Utility/authentication campaigns have no tier-headroom ceiling to chunk against (spec §4.7:
    // marketing has no volume discounts, and correspondingly the messaging-limits tier applies
    // ONLY to marketing-initiated sends) — still bounded per tick so a very large non-marketing
    // broadcast doesn't try to claim its entire audience in one pass. Not spec-mandated; a
    // documented, pragmatic v1 cap.
    private const int NonMarketingBatchSize = 500;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _adminConnectionString;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _pollInterval;
    private readonly ILogger<CampaignChunkerService> _logger;

    public CampaignChunkerService(
        IConfiguration configuration, IServiceScopeFactory scopeFactory, ILogger<CampaignChunkerService> logger)
    {
        _adminConnectionString = configuration.GetConnectionString("Admin")
            ?? throw new InvalidOperationException("ConnectionStrings:Admin is not configured.");
        _scopeFactory = scopeFactory;
        _pollInterval = TimeSpan.FromSeconds(configuration.GetValue("Campaigns:ChunkerIntervalSeconds", 15));
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

    private async Task RunOneTickAsync(CancellationToken ct)
    {
        var due = await ListDueCampaignsAsync(ct);
        foreach (var (campaignId, tenantId) in due)
        {
            try
            {
                await ProcessCampaignAsync(campaignId, tenantId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogCampaignFailed(_logger, campaignId, ex);
            }
        }
    }

    /// <summary>Cross-tenant discovery only — see the class doc comment. Also picks up 'scheduled'
    /// campaigns whose scheduled_at has arrived, so a caller-supplied schedule auto-launches
    /// without a separate scheduler background service (reuse: one tick loop covers both).</summary>
    private async Task<List<(Guid CampaignId, Guid TenantId)>> ListDueCampaignsAsync(CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync(ct);

        await using var command = new NpgsqlCommand(
            """
            SELECT id, tenant_id FROM messaging.campaigns
            WHERE status = 'running' OR (status = 'scheduled' AND scheduled_at <= now())
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(ct);

        var results = new List<(Guid, Guid)>();
        while (await reader.ReadAsync(ct))
        {
            results.Add((reader.GetGuid(0), reader.GetGuid(1)));
        }
        return results;
    }

    private async Task ProcessCampaignAsync(Guid campaignId, Guid tenantId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();

        // RLS override for the rest of this scope — messaging.campaigns/campaign_recipients,
        // waba.phone_numbers, templates.templates/template_versions, and quality.guardian_incidents
        // are all strict-RLS. Same mechanism OutboxDispatcherService already uses.
        if (scope.ServiceProvider.GetRequiredService<ICurrentTenant>() is ScopedCurrentTenant scopedTenant)
        {
            scopedTenant.OverrideTenantId = tenantId;
        }

        var db = scope.ServiceProvider.GetRequiredService<IWaGatewayDbContext>();
        var now = DateTimeOffset.UtcNow;

        var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == campaignId, ct);
        if (campaign is null || campaign.Status is "completed" or "cancelled" or "failed")
        {
            return; // raced with a cancel/complete since discovery — nothing to do
        }

        if (campaign.Status == "scheduled")
        {
            campaign.Status = "running";
            campaign.StartedAt ??= now;
            campaign.UpdatedAt = now;
            campaign.Version += 1;
            await db.SaveChangesAsync(ct);
        }

        var phoneNumber = await db.WabaPhoneNumbers.AsNoTracking().FirstOrDefaultAsync(p => p.Id == campaign.PhoneNumberId, ct);
        if (phoneNumber is null)
        {
            LogMissingPhoneNumber(_logger, campaignId, campaign.PhoneNumberId);
            return; // shouldn't happen (FK RESTRICT) — defensive only
        }

        var templateVersion = await db.TemplateVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == campaign.TemplateVersionId, ct);
        var template = templateVersion is null ? null : await db.Templates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == templateVersion.TemplateId, ct);
        if (templateVersion is null || template is null)
        {
            LogMissingTemplate(_logger, campaignId, campaign.TemplateVersionId);
            return; // shouldn't happen (FK RESTRICT) — defensive only
        }

        if (template.Status == "DISABLED")
        {
            await FailCampaignAsync(db, campaign, now, ct);
            return;
        }

        var pendingCount = await db.CampaignRecipients.CountAsync(r => r.CampaignId == campaign.Id && r.Status == "pending", ct);
        if (pendingCount == 0)
        {
            await TryCompleteCampaignAsync(db, campaign, now, ct);
            return;
        }

        if (template.Status == "PAUSED")
        {
            LogTemplatePaused(_logger, campaignId, template.Id);
            return; // skip this tick only — resumes automatically once unpaused
        }

        var isMarketing = string.Equals(template.Category, "marketing", StringComparison.Ordinal);
        int chunkSize;
        if (isMarketing)
        {
            var throttleAction = await db.GuardianIncidents.AsNoTracking()
                .Where(i => i.PhoneNumberId == phoneNumber.Id && i.Status != "resolved")
                .OrderByDescending(i => i.OpenedAt)
                .Select(i => i.ThrottleAction)
                .FirstOrDefaultAsync(ct);

            if (GuardianThrottleRules.IsFrozen(throttleAction))
            {
                LogGuardianFrozen(_logger, campaignId, phoneNumber.Id);
                return; // skip this tick only — recipients stay pending, resumes once resolved
            }

            var dailyLimit = CampaignTierRules.DailyLimitForRawTier(phoneNumber.MessagingTier);
            var consumed = await ComputeConsumedMarketingUniqueRecipientsAsync(db, tenantId, phoneNumber.Id, now.AddHours(-24), ct);
            chunkSize = CampaignTierRules.ComputeChunkSize(dailyLimit, consumed, throttleAction, pendingCount);
        }
        else
        {
            chunkSize = Math.Min(pendingCount, NonMarketingBatchSize);
        }

        if (chunkSize <= 0)
        {
            return;
        }

        var claimed = await db.CampaignRecipients
            .Where(r => r.CampaignId == campaign.Id && r.Status == "pending")
            .OrderBy(r => r.CreatedAt)
            .Take(chunkSize)
            .ToListAsync(ct);

        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        foreach (var recipient in claimed)
        {
            await DispatchOneRecipientAsync(db, dispatcher, campaign, template, recipient, ct);
        }

        await TryCompleteCampaignAsync(db, campaign, DateTimeOffset.UtcNow, ct);
    }

    /// <summary>
    /// Pushes one recipient through <see cref="SendMessageCommand"/> — the same accept path as an
    /// ad hoc <c>POST /v1/messages</c> send (issue #14) — then fences the recipient's own status
    /// transition on <c>status = 'pending'</c> so a concurrent double-claim (see class doc comment)
    /// can never double-count a campaign counter even though the send itself may have been
    /// attempted twice (harmless: deterministic idempotency key).
    /// </summary>
    private static async Task DispatchOneRecipientAsync(
        IWaGatewayDbContext db,
        IDispatcher dispatcher,
        wavio.SharedDataModel.Entities.Messaging.Campaign campaign,
        wavio.SharedDataModel.Entities.Templates.Template template,
        wavio.SharedDataModel.Entities.Messaging.CampaignRecipient recipient,
        CancellationToken ct)
    {
        var idempotencyKey = $"campaign:{campaign.Id:N}:{recipient.Id:N}";
        var payload = new TemplatePayload(template.Name, template.Language, template.Category, recipient.Params ?? campaign.Params);
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);

        SendMessageResultDto result;
        try
        {
            result = await dispatcher.SendAsync(
                new SendMessageCommand(campaign.TenantId, campaign.PhoneNumberId, recipient.WaId, MessageTypes.Template, payloadJson, idempotencyKey),
                ct);
        }
        catch (KeyNotFoundException)
        {
            // Shouldn't happen — campaigns_phone_number_id_fkey is ON DELETE RESTRICT, so the
            // phone number can't disappear out from under a live campaign. Defensive only.
            await TransitionRecipientAsync(db, recipient.Id, recipient.CampaignId, "failed", "UNRESOLVED_PHONE_NUMBER", null, ct);
            return;
        }

        var newStatus = result.Status switch
        {
            "rejected" when result.ErrorCode == "SUPPRESSED" => "suppressed",
            "rejected" => "failed",
            _ => "sent",
        };

        await TransitionRecipientAsync(db, recipient.Id, recipient.CampaignId, newStatus, result.ErrorCode, result.Id, ct);
    }

    /// <summary>Fenced on status='pending' — see the class doc comment's CLAIM RACE note. Only
    /// increments the campaign counter when this call actually won the race.</summary>
    private static async Task TransitionRecipientAsync(
        IWaGatewayDbContext db, Guid recipientId, Guid campaignId, string newStatus, string? errorCode, Guid? outboundMessageId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var rowsAffected = await db.CampaignRecipients
            .Where(r => r.Id == recipientId && r.Status == "pending")
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.Status, newStatus)
                .SetProperty(r => r.ErrorCode, errorCode)
                .SetProperty(r => r.OutboundMessageId, outboundMessageId)
                .SetProperty(r => r.ProcessedAt, now)
                .SetProperty(r => r.UpdatedAt, now)
                .SetProperty(r => r.Version, r => r.Version + 1), ct);

        if (rowsAffected == 0)
        {
            return; // lost the claim race — whoever won already owns (and counted) this outcome
        }

        switch (newStatus)
        {
            case "sent":
                await db.Campaigns.Where(c => c.Id == campaignId)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.SentCount, c => c.SentCount + 1).SetProperty(c => c.UpdatedAt, now), ct);
                break;
            case "suppressed":
                await db.Campaigns.Where(c => c.Id == campaignId)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.SuppressedCount, c => c.SuppressedCount + 1).SetProperty(c => c.UpdatedAt, now), ct);
                break;
            case "failed":
                await db.Campaigns.Where(c => c.Id == campaignId)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.FailedCount, c => c.FailedCount + 1).SetProperty(c => c.UpdatedAt, now), ct);
                break;
        }
    }

    /// <summary>Pragmatic v1 counter (documented, per the issue brief's own framing): loads the
    /// trailing-24h template sends for this phone number (bounded by tenant + phone number + time
    /// window + an indexed accepted_at range — never a full table scan) and filters to marketing
    /// category / distinct recipients IN MEMORY, same "deserialize the jsonb payload in C#" idiom
    /// as <c>OutboxDispatcherService.IsMarketingTemplate</c> — there is no jsonb-indexed category
    /// column to push this down to SQL with. A future perf pass could add a generated column or a
    /// dedicated per-number/day counter table if this ever shows up as a hot path; flagged here,
    /// not silently accepted as fine forever.</summary>
    private static async Task<int> ComputeConsumedMarketingUniqueRecipientsAsync(
        IWaGatewayDbContext db, Guid tenantId, Guid phoneNumberId, DateTimeOffset cutoff, CancellationToken ct)
    {
        var candidates = await db.OutboundMessages.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.PhoneNumberId == phoneNumberId &&
                        m.MessageType == MessageTypes.Template && m.AcceptedAt >= cutoff && m.Status != "rejected")
            .Select(m => new { m.ToWaId, m.Payload })
            .ToListAsync(ct);

        var uniqueMarketingRecipients = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<TemplatePayload>(candidate.Payload, JsonOptions);
                if (payload?.Category == "marketing")
                {
                    uniqueMarketingRecipients.Add(candidate.ToWaId);
                }
            }
            catch (JsonException)
            {
                // Malformed payload — can't have been a valid marketing template send either way.
            }
        }
        return uniqueMarketingRecipients.Count;
    }

    private static async Task TryCompleteCampaignAsync(
        IWaGatewayDbContext db, wavio.SharedDataModel.Entities.Messaging.Campaign campaign, DateTimeOffset now, CancellationToken ct)
    {
        if (campaign.Status != "running")
        {
            return;
        }

        var pendingOrSent = await db.CampaignRecipients.CountAsync(
            r => r.CampaignId == campaign.Id && (r.Status == "pending" || r.Status == "sent"), ct);
        if (!CampaignRecipientStatusRules.IsCampaignComplete(pendingOrSent))
        {
            return;
        }

        await db.Campaigns.Where(c => c.Id == campaign.Id && c.Status == "running")
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, "completed")
                .SetProperty(c => c.CompletedAt, now)
                .SetProperty(c => c.UpdatedAt, now)
                .SetProperty(c => c.Version, c => c.Version + 1), ct);
    }

    private static async Task FailCampaignAsync(
        IWaGatewayDbContext db, wavio.SharedDataModel.Entities.Messaging.Campaign campaign, DateTimeOffset now, CancellationToken ct)
    {
        await db.CampaignRecipients
            .Where(r => r.CampaignId == campaign.Id && r.Status == "pending")
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, "cancelled")
                .SetProperty(r => r.ProcessedAt, now)
                .SetProperty(r => r.UpdatedAt, now), ct);

        campaign.Status = "failed";
        campaign.CompletedAt = now;
        campaign.UpdatedAt = now;
        campaign.Version += 1;
        await db.SaveChangesAsync(ct);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Campaign chunker tick failed")]
    private static partial void LogTickFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Processing campaign {CampaignId} failed this tick — will retry next tick")]
    private static partial void LogCampaignFailed(ILogger logger, Guid campaignId, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Campaign {CampaignId} references phone number {PhoneNumberId} with no matching waba.phone_numbers row")]
    private static partial void LogMissingPhoneNumber(ILogger logger, Guid campaignId, Guid phoneNumberId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Campaign {CampaignId} references template version {TemplateVersionId} with no matching template row")]
    private static partial void LogMissingTemplate(ILogger logger, Guid campaignId, Guid templateVersionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Campaign {CampaignId}'s pinned template {TemplateId} is PAUSED — skipping this tick")]
    private static partial void LogTemplatePaused(ILogger logger, Guid campaignId, Guid templateId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Campaign {CampaignId}: Guardian has frozen marketing sends for phone number {PhoneNumberId} — skipping this tick")]
    private static partial void LogGuardianFrozen(ILogger logger, Guid campaignId, Guid phoneNumberId);
}
