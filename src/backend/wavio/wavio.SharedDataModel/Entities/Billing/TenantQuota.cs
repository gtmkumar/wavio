namespace wavio.SharedDataModel.Entities.Billing;

/// <summary>
/// Per-tenant metering limit for a category/period (billing.tenant_quotas, issue #19, spec §4.7).
/// Soft limit -> alert (never blocks); hard limit -> block, but ONLY for marketing — utility,
/// authentication and service are never blocked (spec §4.7, hard platform rule enforced in
/// WaBilling.Application's QuotaRules regardless of which quota row trips). Tenant-scoped, RLS.
/// </summary>
public class TenantQuota
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>marketing | utility | authentication | service | all.</summary>
    public string Category { get; set; } = null!;

    /// <summary>daily | monthly.</summary>
    public string Period { get; set; } = "monthly";

    /// <summary>messages | amount.</summary>
    public string LimitUnit { get; set; } = "messages";

    public long? SoftLimit { get; set; }
    public long? HardLimit { get; set; }
    public string? Currency { get; set; }
    public bool Enabled { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public int Version { get; set; }
}
