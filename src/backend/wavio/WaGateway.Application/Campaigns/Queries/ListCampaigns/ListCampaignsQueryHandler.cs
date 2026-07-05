using WaGateway.Application.Campaigns.Dtos;
using WaGateway.Application.Common.Interfaces;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace WaGateway.Application.Campaigns.Queries.ListCampaigns;

public sealed class ListCampaignsQueryHandler : IQueryHandler<ListCampaignsQuery, IReadOnlyList<CampaignListItemDto>>
{
    private const int MaxRows = 200;

    private readonly IWaGatewayDbContext _db;

    public ListCampaignsQueryHandler(IWaGatewayDbContext db) => _db = db;

    public async Task<IReadOnlyList<CampaignListItemDto>> HandleAsync(ListCampaignsQuery query, CancellationToken cancellationToken)
    {
        var campaigns = _db.Campaigns.AsNoTracking().Where(c => c.TenantId == query.TenantId);
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            campaigns = campaigns.Where(c => c.Status == query.Status);
        }

        return await campaigns
            .OrderByDescending(c => c.CreatedAt)
            .Take(MaxRows)
            .Select(c => new CampaignListItemDto(
                c.Id, c.Name, c.Status, c.AudienceCount, c.SentCount, c.DeliveredCount, c.ReadCount, c.FailedCount, c.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
