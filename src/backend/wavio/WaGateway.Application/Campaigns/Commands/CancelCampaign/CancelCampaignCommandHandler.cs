using WaGateway.Application.Campaigns.Commands.CreateCampaign;
using WaGateway.Application.Campaigns.Dtos;
using WaGateway.Application.Common.Interfaces;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.Utilities.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace WaGateway.Application.Campaigns.Commands.CancelCampaign;

public sealed class CancelCampaignCommandHandler : ICommandHandler<CancelCampaignCommand, CampaignDto>
{
    private static readonly string[] TerminalStatuses = ["completed", "cancelled", "failed"];

    private readonly IWaGatewayDbContext _db;

    public CancelCampaignCommandHandler(IWaGatewayDbContext db) => _db = db;

    public async Task<CampaignDto> HandleAsync(CancelCampaignCommand command, CancellationToken cancellationToken)
    {
        var campaign = await _db.Campaigns
            .FirstOrDefaultAsync(c => c.Id == command.CampaignId && c.TenantId == command.TenantId, cancellationToken)
            ?? throw new KeyNotFoundException($"Campaign {command.CampaignId} was not found for this tenant.");

        if (TerminalStatuses.Contains(campaign.Status))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["status"] = [$"Campaign is already terminal (current status: {campaign.Status})."]
            });
        }

        var now = DateTimeOffset.UtcNow;

        // Tracked-entity mutation + one SaveChangesAsync (matching every other handler's
        // convention in this codebase — a conditional ExecuteUpdateAsync fenced write, the
        // pattern CampaignChunkerService/CampaignStatusConsumerService use, is unsupported by
        // the EF Core InMemory provider this handler's own tests run against). This leaves a
        // narrow, documented Wave 1 race: a recipient the chunker claims and dispatches in the
        // instant between this query and SaveChangesAsync could have its status overwritten back
        // to 'cancelled' here (last-write-wins, no concurrency token on campaign_recipients).
        // The recipient's own outbound_messages/outbox rows are never touched by this handler, so
        // the worst outcome is a stale recipient-status/counter mismatch, not a double dispatch or
        // lost send — acceptable for a human-triggered, low-frequency action, and no worse than
        // the accepted limitations already documented for MessagingTierGate/TokenBucketRateLimiter.
        var pendingRecipients = await _db.CampaignRecipients
            .Where(r => r.CampaignId == campaign.Id && r.Status == "pending")
            .ToListAsync(cancellationToken);
        foreach (var recipient in pendingRecipients)
        {
            recipient.Status = "cancelled";
            recipient.ProcessedAt = now;
            recipient.UpdatedAt = now;
            recipient.Version += 1;
        }

        campaign.Status = "cancelled";
        campaign.UpdatedAt = now;
        campaign.Version += 1;
        await _db.SaveChangesAsync(cancellationToken);

        return CreateCampaignCommandHandler.ToDto(campaign, failureBreakdown: null);
    }
}
