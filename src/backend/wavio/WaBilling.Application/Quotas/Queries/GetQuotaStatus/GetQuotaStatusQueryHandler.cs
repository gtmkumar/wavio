using WaBilling.Application.Common.Interfaces;
using WaBilling.Application.Common.Logic;
using WaBilling.Application.Quotas.Dtos;
using WaBilling.Application.Quotas.Logic;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace WaBilling.Application.Quotas.Queries.GetQuotaStatus;

public sealed class GetQuotaStatusQueryHandler : IQueryHandler<GetQuotaStatusQuery, IReadOnlyList<QuotaStatusEntryDto>>
{
    private readonly IWaBillingDbContext _db;

    public GetQuotaStatusQueryHandler(IWaBillingDbContext db) => _db = db;

    public async Task<IReadOnlyList<QuotaStatusEntryDto>> HandleAsync(
        GetQuotaStatusQuery query, CancellationToken cancellationToken)
    {
        var quotas = await _db.TenantQuotas.AsNoTracking()
            .Where(q => q.TenantId == query.TenantId && q.Enabled)
            .ToListAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var results = new List<QuotaStatusEntryDto>(quotas.Count);

        foreach (var quota in quotas)
        {
            var periodStart = BillingPeriods.PeriodStart(quota.Period, now);
            var counter = await _db.UsageCounters.AsNoTracking().FirstOrDefaultAsync(
                u => u.TenantId == query.TenantId && u.Category == quota.Category
                  && u.Period == quota.Period && u.PeriodStart == periodStart,
                cancellationToken);

            var currentValue = QuotaRules.CurrentValue(quota.LimitUnit, counter);

            results.Add(new QuotaStatusEntryDto(
                quota.Category, quota.Period, quota.LimitUnit, quota.SoftLimit, quota.HardLimit,
                currentValue, counter?.SoftLimitAlertedAt is not null, counter?.HardLimitBlockedAt is not null));
        }

        return results;
    }
}
