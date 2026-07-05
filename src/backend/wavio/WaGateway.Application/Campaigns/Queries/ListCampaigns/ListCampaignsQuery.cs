using WaGateway.Application.Campaigns.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaGateway.Application.Campaigns.Queries.ListCampaigns;

/// <summary><c>GET /v1/campaigns</c> (issue #22) — newest first, optionally filtered by status.
/// No pagination parameter — matches WaBilling's <c>GetRateCardsQuery</c> precedent (this
/// codebase has no established list-pagination convention to follow); capped at 200 rows in the
/// handler rather than inventing a paging scheme unprompted.</summary>
public sealed record ListCampaignsQuery(Guid TenantId, string? Status) : IQuery<IReadOnlyList<CampaignListItemDto>>;
