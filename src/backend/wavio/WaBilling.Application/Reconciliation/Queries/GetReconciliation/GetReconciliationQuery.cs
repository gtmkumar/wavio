using WaBilling.Application.Reconciliation.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaBilling.Application.Reconciliation.Queries.GetReconciliation;

/// <summary>GET /v1/reconciliation?periodStart=&amp;periodEnd= — ledger vs. invoice-feed variance
/// for a tenant's billing period.</summary>
public sealed record GetReconciliationQuery(Guid TenantId, DateOnly PeriodStart, DateOnly PeriodEnd)
    : IQuery<ReconciliationDto>;
