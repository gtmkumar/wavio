namespace wavio.SharedDataModel.Entities.Quality;

/// <summary>
/// Weekly per-number health rollup (quality.health_snapshots, issue #20, spec §4.6,
/// db/migrations/V011__quality.sql). Append-only, one row per (phone_number_id, period_start) —
/// written by <c>HealthSnapshotRollupService</c>. Rates are percentages (0-100, 2dp) computed by
/// <c>WaIntel.Application.Quality.Logic.HealthMetricsRules</c>.
///
/// <c>block_proxy_rate</c> is a PROXY metric, not Meta's true block rate (spec §4.6 calls it
/// "block proxy" for exactly this reason) — the Cloud API does not expose a direct
/// blocked-by-user count, so the failed-status ratio is used as the closest available signal.
/// Documented v1 limitation, not a bug.
/// </summary>
public class HealthSnapshot
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PhoneNumberId { get; set; }

    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }

    public decimal? DeliveryRate { get; set; }
    public decimal? ReadRate { get; set; }
    public decimal? BlockProxyRate { get; set; }

    /// <summary>green | yellow | red | unknown (lowercase) as of snapshot time.</summary>
    public string? QualityRating { get; set; }

    /// <summary>Canonical lowercase tier (tier_250/tier_1k/...) as of snapshot time.</summary>
    public string? MessagingTier { get; set; }

    /// <summary>Remaining daily-volume headroom to the current tier's ceiling; null = unlimited
    /// tier (no ceiling).</summary>
    public long? TierHeadroom { get; set; }

    public long MessagesSent { get; set; }
    public long MessagesDelivered { get; set; }
    public long MessagesRead { get; set; }
    public long MessagesFailed { get; set; }

    /// <summary>Additional computed metrics (jsonb) — extensibility without a schema change.</summary>
    public string? Metrics { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}
