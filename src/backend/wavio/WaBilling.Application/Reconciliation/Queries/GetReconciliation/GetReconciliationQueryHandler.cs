using WaBilling.Application.Common.Interfaces;
using WaBilling.Application.Reconciliation.Dtos;
using WaBilling.Application.Reconciliation.Logic;
using wavio.Utilities.Exceptions;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;
using ValidationException = wavio.Utilities.Exceptions.ValidationException;

namespace WaBilling.Application.Reconciliation.Queries.GetReconciliation;

public sealed class GetReconciliationQueryHandler
    : IQueryHandler<GetReconciliationQuery, ReconciliationDto>
{
    private readonly IWaBillingDbContext _db;

    public GetReconciliationQueryHandler(IWaBillingDbContext db) => _db = db;

    public async Task<ReconciliationDto> HandleAsync(GetReconciliationQuery query, CancellationToken cancellationToken)
    {
        if (query.PeriodEnd < query.PeriodStart)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["periodEnd"] = ["periodEnd must be on or after periodStart."]
            });

        var periodStartAt = query.PeriodStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        // Exclusive upper bound at the start of the day AFTER periodEnd — billed_at is a
        // timestamptz, so a plain "<= periodEnd" would silently drop same-day rows billed after
        // midnight-UTC of periodEnd itself.
        var periodEndExclusiveAt = query.PeriodEnd.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var ledgerCosts = await _db.MessageCosts.AsNoTracking()
            .Where(m => m.TenantId == query.TenantId && m.Billable
                     && m.BilledAt >= periodStartAt && m.BilledAt < periodEndExclusiveAt)
            .Select(m => m.Amount)
            .ToListAsync(cancellationToken);

        var invoices = await _db.InvoicesFeed.AsNoTracking()
            .Where(i => i.TenantId == query.TenantId
                     && i.PeriodStart >= query.PeriodStart && i.PeriodEnd <= query.PeriodEnd)
            .Select(i => i.TotalAmount)
            .ToListAsync(cancellationToken);

        var ledgerTotal = ledgerCosts.Sum();
        var invoiceTotal = invoices.Sum();
        var (varianceAmount, variancePercent, withinTarget) =
            ReconciliationCalculator.Evaluate(ledgerTotal, invoiceTotal);

        return new ReconciliationDto(
            query.PeriodStart, query.PeriodEnd, ledgerTotal, ledgerCosts.Count,
            invoiceTotal, invoices.Count, varianceAmount, variancePercent, withinTarget);
    }
}
