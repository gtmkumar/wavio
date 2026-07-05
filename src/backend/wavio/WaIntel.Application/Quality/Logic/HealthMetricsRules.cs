namespace WaIntel.Application.Quality.Logic;

/// <summary>
/// Pure health-metric rate computations (issue #20, spec §4.6 weekly per-number health report). No
/// I/O — callers load the raw counts and pass them in.
///
/// <see cref="BlockProxyRate"/> is a documented v1 PROXY, not Meta's true block rate: the Cloud
/// API does not expose a direct "blocked by user" count, so the failed-delivery ratio is used as
/// the closest available signal (spec §4.6 itself calls this "block proxy" for exactly this
/// reason).
/// </summary>
public static class HealthMetricsRules
{
    /// <summary>Block-rate telemetry threshold (spec §4.6) — a block-proxy rate at or above this
    /// is flagged as a spike worth opening a <c>block_rate_spike</c> incident for. Picked as a
    /// pragmatic v1 default, not derived from a documented Meta SLA (none published).</summary>
    public const decimal BlockRateSpikeThresholdPercent = 15m;

    public static decimal DeliveryRate(long sent, long delivered) => Rate(delivered, sent);

    public static decimal ReadRate(long delivered, long read) => Rate(read, delivered);

    public static decimal BlockProxyRate(long sent, long failed) => Rate(failed, sent);

    public static bool IsBlockRateSpike(decimal blockProxyRatePercent) =>
        blockProxyRatePercent >= BlockRateSpikeThresholdPercent;

    /// <summary>0 when the denominator is 0 — an empty period isn't a 0% or 100% rate, but 0% is
    /// the least misleading default for a rollup row (avoids implying either perfect or total
    /// failure when nothing was sent at all).</summary>
    private static decimal Rate(long numerator, long denominator) =>
        denominator <= 0 ? 0m : Math.Round((decimal)numerator / denominator * 100m, 2);
}
