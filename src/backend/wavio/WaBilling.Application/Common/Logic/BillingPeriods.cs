namespace WaBilling.Application.Common.Logic;

/// <summary>Shared period-bucketing for <c>billing.usage_counters</c> (issue #19) — the cost
/// ledger consumer and the quota check/status handlers must compute the exact same
/// <c>period_start</c> for "now" or they'd silently read/write different buckets.</summary>
public static class BillingPeriods
{
    public const string Daily = "daily";
    public const string Monthly = "monthly";

    public static DateOnly PeriodStart(string period, DateTimeOffset asOf) => period switch
    {
        Daily => DateOnly.FromDateTime(asOf.UtcDateTime),
        Monthly => new DateOnly(asOf.UtcDateTime.Year, asOf.UtcDateTime.Month, 1),
        _ => throw new ArgumentOutOfRangeException(
            nameof(period), period, "period must be 'daily' or 'monthly'.")
    };
}
