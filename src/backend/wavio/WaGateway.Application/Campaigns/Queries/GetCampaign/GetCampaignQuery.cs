using WaGateway.Application.Campaigns.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaGateway.Application.Campaigns.Queries.GetCampaign;

/// <summary><c>GET /v1/campaigns/{id}</c> (issue #22) — progress counters + a failure breakdown
/// grouped by <c>campaign_recipients.error_code</c>.</summary>
public sealed record GetCampaignQuery(Guid TenantId, Guid CampaignId) : IQuery<CampaignDto>;
