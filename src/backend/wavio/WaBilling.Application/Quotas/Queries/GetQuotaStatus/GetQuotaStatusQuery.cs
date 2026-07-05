using WaBilling.Application.Quotas.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaBilling.Application.Quotas.Queries.GetQuotaStatus;

/// <summary>GET /v1/quotas/status — tenant self-service view of current usage vs. configured
/// quotas. Read-only: unlike CheckQuotaCommand, this never stamps alert/block timestamps.</summary>
public sealed record GetQuotaStatusQuery(Guid TenantId) : IQuery<IReadOnlyList<QuotaStatusEntryDto>>;
