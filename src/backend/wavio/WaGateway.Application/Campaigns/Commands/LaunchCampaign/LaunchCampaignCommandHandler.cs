using WaGateway.Application.Campaigns.Commands.CreateCampaign;
using WaGateway.Application.Campaigns.Dtos;
using WaGateway.Application.Common.Interfaces;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.Utilities.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace WaGateway.Application.Campaigns.Commands.LaunchCampaign;

public sealed class LaunchCampaignCommandHandler : ICommandHandler<LaunchCampaignCommand, CampaignDto>
{
    private static readonly string[] LaunchableStatuses = ["draft", "scheduled"];

    private readonly IWaGatewayDbContext _db;

    public LaunchCampaignCommandHandler(IWaGatewayDbContext db) => _db = db;

    public async Task<CampaignDto> HandleAsync(LaunchCampaignCommand command, CancellationToken cancellationToken)
    {
        var campaign = await _db.Campaigns
            .FirstOrDefaultAsync(c => c.Id == command.CampaignId && c.TenantId == command.TenantId, cancellationToken)
            ?? throw new KeyNotFoundException($"Campaign {command.CampaignId} was not found for this tenant.");

        if (!LaunchableStatuses.Contains(campaign.Status))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["status"] = [$"Campaign must be draft or scheduled to launch (current status: {campaign.Status})."]
            });
        }

        var templateVersion = await _db.TemplateVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == campaign.TemplateVersionId, cancellationToken);
        var template = templateVersion is null ? null : await _db.Templates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == templateVersion.TemplateId, cancellationToken);

        // DISABLED is terminal in the template state machine (db/migrations/V009's own comment) —
        // a campaign can never dispatch against it, so reject the launch outright rather than
        // starting a campaign the chunker would immediately fail. PAUSED is allowed to launch:
        // Guardian's freeze there is meant to be temporary (V009: "Guardian ... freeze campaigns
        // using paused template") — the chunker skips ticks while paused and auto-resumes once
        // unpaused, same "throttling is not a failure" treatment as the Guardian quality freeze.
        if (template?.Status == "DISABLED")
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["templateVersionId"] = ["The pinned template is DISABLED and can never be dispatched."]
            });
        }

        var now = DateTimeOffset.UtcNow;
        campaign.Status = "running";
        campaign.StartedAt ??= now;
        campaign.UpdatedAt = now;
        campaign.Version += 1;
        await _db.SaveChangesAsync(cancellationToken);

        return CreateCampaignCommandHandler.ToDto(campaign, failureBreakdown: null);
    }
}
