using WaGateway.Application.Campaigns.Dtos;
using WaGateway.Application.Common.Interfaces;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.SharedDataModel.Entities.Messaging;
using wavio.Utilities.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace WaGateway.Application.Campaigns.Commands.CreateCampaign;

/// <summary>
/// Creates a campaign + its per-recipient fan-out rows (issue #22). Deny-wins suppression
/// (spec §4.10) is applied up-front here — a suppressed recipient's row is written straight to
/// 'suppressed', never 'pending', so the chunker never has to re-derive it — but ONLY for a
/// marketing-category pinned template, mirroring <c>SendMessageHandler</c>'s own
/// "utility/authentication/service sends are never blocked by suppression" rule exactly.
///
/// Unlike an ad hoc <c>POST /v1/messages</c> send (issue #14, where the caller declares category),
/// a campaign's category is authoritatively the PINNED TEMPLATE's own <c>templates.templates.category</c>
/// — resolved here, not caller-declared — closing the "category is caller-declared" gap issue #14's
/// decisions memory flagged as a Wave 1 scope cut, now that a campaign always has a real template
/// to look up.
/// </summary>
public sealed class CreateCampaignCommandHandler : ICommandHandler<CreateCampaignCommand, CampaignDto>
{
    // Single-market v1 (mirrors WaBilling's own DefaultCurrency=INR judgment call, issue #19) —
    // only feeds the advisory pre-launch estimate, never stored per recipient.
    private const string DefaultCountry = "IN";

    private readonly IWaGatewayDbContext _db;
    private readonly IBillingEstimatorClient _estimator;

    public CreateCampaignCommandHandler(IWaGatewayDbContext db, IBillingEstimatorClient estimator)
    {
        _db = db;
        _estimator = estimator;
    }

    public async Task<CampaignDto> HandleAsync(CreateCampaignCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["name"] = ["name is required."] });
        }
        if (command.Audience.Count == 0)
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["audience"] = ["audience must have at least one recipient."] });
        }
        var duplicateWaIds = command.Audience.GroupBy(a => a.WaId).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicateWaIds.Count > 0)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["audience"] = [$"duplicate waId(s) in the audience: {string.Join(", ", duplicateWaIds)}."]
            });
        }

        // Phone number ownership — same synchronous 404-not-202-then-async-dead-letter reasoning
        // as SendMessageHandler's S3 fix (issue #14 security review, PR #45).
        var phoneNumberBelongsToTenant = await _db.WabaPhoneNumbers
            .AnyAsync(p => p.TenantId == command.TenantId && p.Id == command.PhoneNumberId, cancellationToken);
        if (!phoneNumberBelongsToTenant)
        {
            throw new KeyNotFoundException($"Phone number {command.PhoneNumberId} was not found for this tenant.");
        }

        var templateVersion = await _db.TemplateVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == command.TemplateVersionId && v.TenantId == command.TenantId, cancellationToken);
        if (templateVersion is null)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["templateVersionId"] = ["No matching template version was found for this tenant."]
            });
        }
        if (templateVersion.Status != "APPROVED")
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["templateVersionId"] = [$"Template version must be APPROVED to pin to a campaign (current status: {templateVersion.Status})."]
            });
        }

        var template = await _db.Templates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == templateVersion.TemplateId && t.TenantId == command.TenantId, cancellationToken);
        if (template is null)
        {
            // Unreachable under normal operation (template_versions_template_id_fkey guarantees
            // the parent row exists) — defensive only, matching SendMessageHandler's idempotency
            // "should be unreachable" convention.
            throw new InvalidOperationException(
                $"Template version {templateVersion.Id} has no matching parent template row — this should be unreachable.");
        }

        var isMarketing = string.Equals(template.Category, "marketing", StringComparison.Ordinal);

        var audienceWaIds = command.Audience.Select(a => a.WaId).ToList();
        var now = DateTimeOffset.UtcNow;
        var suppressedWaIds = isMarketing
            ? (await _db.SuppressionListEntries.AsNoTracking()
                .Where(s => s.TenantId == command.TenantId && audienceWaIds.Contains(s.WaId) &&
                            (s.ExpiresAt == null || s.ExpiresAt > now))
                .Select(s => s.WaId)
                .ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal)
            : [];

        var country = string.IsNullOrWhiteSpace(command.Country) ? DefaultCountry : command.Country;
        var estimate = await _estimator.EstimateAsync(template.Category, country, command.PhoneNumberId, cancellationToken);

        var campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            TenantId = command.TenantId,
            PhoneNumberId = command.PhoneNumberId,
            Name = command.Name,
            TemplateVersionId = command.TemplateVersionId,
            Params = command.ParamsJson,
            Status = "draft",
            ScheduledAt = command.ScheduledAt,
            AudienceCount = command.Audience.Count,
            SuppressedCount = suppressedWaIds.Count,
            // Advisory only (spec §4.3) — left null when the estimator was unreachable or had no
            // priced entry, never a silently-wrong zero (mirrors CostEstimateDto.Found's contract).
            ProjectedCost = estimate is { Found: true, Billable: true }
                ? estimate.Amount * (command.Audience.Count - suppressedWaIds.Count)
                : null,
            ProjectedCurrency = estimate is { Found: true } ? estimate.Currency : null,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
        _db.Campaigns.Add(campaign);

        foreach (var member in command.Audience)
        {
            var isSuppressed = suppressedWaIds.Contains(member.WaId);
            _db.CampaignRecipients.Add(new CampaignRecipient
            {
                Id = Guid.NewGuid(),
                TenantId = command.TenantId,
                CampaignId = campaign.Id,
                WaId = member.WaId,
                Params = member.ParamsJson,
                Status = isSuppressed ? "suppressed" : "pending",
                ProcessedAt = isSuppressed ? now : null,
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        return ToDto(campaign, failureBreakdown: null);
    }

    internal static CampaignDto ToDto(Campaign campaign, IReadOnlyDictionary<string, int>? failureBreakdown) =>
        new(
            campaign.Id, campaign.Name, campaign.PhoneNumberId, campaign.TemplateVersionId, campaign.Status,
            campaign.ScheduledAt, campaign.StartedAt, campaign.CompletedAt,
            campaign.AudienceCount, campaign.SuppressedCount, campaign.SentCount,
            campaign.DeliveredCount, campaign.ReadCount, campaign.FailedCount,
            campaign.ProjectedCost, campaign.ProjectedCurrency, failureBreakdown);
}
