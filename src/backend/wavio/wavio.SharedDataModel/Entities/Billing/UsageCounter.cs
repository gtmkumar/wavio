namespace wavio.SharedDataModel.Entities.Billing;

/// <summary>
/// Running metered usage for one (tenant, category, period, period_start) bucket
/// (billing.usage_counters, issue #19, spec §4.7). Upserted onto its unique key by the cost
/// ledger consumer (RecordMessageCostHandler) as each billed message lands; the *_at stamps are
/// set by the quota check (CheckQuotaHandler) the first time a threshold is crossed, so a
/// re-check doesn't re-alert every call. Tenant-scoped, RLS.
/// </summary>
public class UsageCounter
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>marketing | utility | authentication | service | all.</summary>
    public string Category { get; set; } = null!;

    /// <summary>daily | monthly.</summary>
    public string Period { get; set; } = "monthly";

    public DateOnly PeriodStart { get; set; }
    public long MessageCount { get; set; }
    public decimal BillableAmount { get; set; }
    public string Currency { get; set; } = "INR";

    public DateTimeOffset? SoftLimitAlertedAt { get; set; }
    public DateTimeOffset? HardLimitBlockedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public int Version { get; set; }
}
