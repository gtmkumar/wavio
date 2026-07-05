using WaGateway.Application.Campaigns.Commands.CreateCampaign;
using WaGateway.Application.Campaigns.Dtos;
using WaGateway.Application.Common.Interfaces;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace WaGateway.Application.Campaigns.Queries.GetCampaign;

public sealed class GetCampaignQueryHandler : IQueryHandler<GetCampaignQuery, CampaignDto>
{
    private readonly IWaGatewayDbContext _db;

    public GetCampaignQueryHandler(IWaGatewayDbContext db) => _db = db;

    public async Task<CampaignDto> HandleAsync(GetCampaignQuery query, CancellationToken cancellationToken)
    {
        var campaign = await _db.Campaigns.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == query.CampaignId && c.TenantId == query.TenantId, cancellationToken)
            ?? throw new KeyNotFoundException($"Campaign {query.CampaignId} was not found for this tenant.");

        var failureBreakdown = await _db.CampaignRecipients.AsNoTracking()
            .Where(r => r.CampaignId == campaign.Id && r.Status == "failed")
            .GroupBy(r => r.ErrorCode ?? "UNKNOWN")
            .Select(g => new { ErrorCode = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.ErrorCode, g => g.Count, cancellationToken);

        return CreateCampaignCommandHandler.ToDto(campaign, failureBreakdown);
    }
}
