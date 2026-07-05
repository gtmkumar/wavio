using wavio.SharedDataModel.Entities.Billing;

namespace WaBilling.Application.Quotas.Logic;

/// <summary>
/// Pure quota threshold evaluation (issue #19, spec §4.7). No I/O — callers load the quota row
/// and its matching usage counter and pass them in, so this is fully unit-testable.
/// </summary>
public static class QuotaRules
{
    /// <summary>Hard platform rule: only <c>marketing</c> sends can ever be blocked by a quota —
    /// utility, authentication and service are never blocked, no matter which quota row (the
    /// send's own category, or the tenant-wide "all" aggregate) trips (spec §4.7).</summary>
    public const string OnlyBlockableCategory = "marketing";

    /// <summary>The metric a quota row is measured in: message_count for "messages", the running
    /// billable amount for "amount". A missing counter (no usage recorded yet this period) is 0.</summary>
    public static decimal CurrentValue(string limitUnit, UsageCounter? counter)
    {
        if (counter is null) return 0m;
        return limitUnit == "amount" ? counter.BillableAmount : counter.MessageCount;
    }

    public static bool IsSoftBreached(TenantQuota quota, decimal currentValue) =>
        quota.SoftLimit is { } soft && currentValue >= soft;

    /// <summary>Breached once the CURRENT usage already meets the hard limit — i.e. the next
    /// send would be the (hardLimit + 1)th, so it is blocked pre-emptively rather than allowed
    /// and only counted afterward.</summary>
    public static bool IsHardBreached(TenantQuota quota, decimal currentValue) =>
        quota.HardLimit is { } hard && currentValue >= hard;

    /// <summary>Whether a hard breach actually results in a blocked send for this category —
    /// applies the never-block-except-marketing rule on top of a raw breach.</summary>
    public static bool ShouldBlock(string sendCategory, bool hardBreached) =>
        hardBreached && string.Equals(sendCategory, OnlyBlockableCategory, StringComparison.Ordinal);
}
