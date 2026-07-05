namespace WaIntel.Application.Quality.Logic;

/// <summary>Result of <see cref="TierRules.ComputeSafeDailySendPlan"/> — the tier-growth advisor's
/// recommendation (spec §4.6, minimal v1 heuristic — see that method's doc comment).</summary>
public sealed record TierAdvisorPlan(
    string CurrentTier,
    string? NextTier,
    long? CurrentDailyLimit,
    long RecommendedDailyVolume,
    bool ReadyToGrow,
    string Recommendation);

/// <summary>
/// Pure messaging-tier rules (issue #20, spec §4.2/§4.6). Tier codes here are the platform's
/// canonical lowercase form (see <see cref="QualityCodes"/>). No I/O — unit-testable directly.
/// </summary>
public static class TierRules
{
    public const string Tier250 = "tier_250";
    public const string Tier1k = "tier_1k";
    public const string Tier10k = "tier_10k";
    public const string Tier100k = "tier_100k";
    public const string TierUnlimited = "tier_unlimited";

    private static readonly Dictionary<string, int> Rank = new(StringComparer.Ordinal)
    {
        [Tier250] = 0,
        [Tier1k] = 1,
        [Tier10k] = 2,
        [Tier100k] = 3,
        [TierUnlimited] = 4,
    };

    /// <summary>null = unlimited (no ceiling).</summary>
    private static readonly Dictionary<string, long?> DailyLimit = new(StringComparer.Ordinal)
    {
        [Tier250] = 250,
        [Tier1k] = 1_000,
        [Tier10k] = 10_000,
        [Tier100k] = 100_000,
        [TierUnlimited] = null,
    };

    private static readonly Dictionary<string, string?> NextTierMap = new(StringComparer.Ordinal)
    {
        [Tier250] = Tier1k,
        [Tier1k] = Tier10k,
        [Tier10k] = Tier100k,
        [Tier100k] = TierUnlimited,
        [TierUnlimited] = null,
    };

    public static bool TryGetRank(string tier, out int rank) => Rank.TryGetValue(tier, out rank);

    /// <summary>True only when both tiers are recognized and the new one ranks lower. A null
    /// <paramref name="oldTier"/> (no prior tier on record) is never a downgrade — there's nothing
    /// to compare against.</summary>
    public static bool IsDowngrade(string? oldTier, string newTier)
    {
        if (oldTier is null) return false;
        return TryGetRank(oldTier, out var oldRank) && TryGetRank(newTier, out var newRank) && newRank < oldRank;
    }

    public static long? DailyLimitFor(string tier) => DailyLimit.TryGetValue(tier, out var limit) ? limit : null;

    /// <summary>Remaining daily-volume headroom to the current tier's ceiling; null = unlimited
    /// tier (no ceiling to report).</summary>
    public static long? HeadroomFor(string tier, long recentAverageDailyVolume)
    {
        var limit = DailyLimitFor(tier);
        return limit.HasValue ? Math.Max(0, limit.Value - recentAverageDailyVolume) : null;
    }

    /// <summary>
    /// PRAGMATIC V1 HEURISTIC, explicitly not Meta's official graduation algorithm (Meta does not
    /// publish one) — spec §4.6 asks Guardian to "compute a safe daily send plan" toward the next
    /// tier; this is a documented, simple, testable starting point, not a guarantee Meta will
    /// actually grant the next tier.
    ///
    /// Rule: a tenant is "ready to grow" once its recent average daily volume has reached at least
    /// 80% of the CURRENT tier's daily limit AND the current quality rating is green (never advise
    /// growth on a degraded number). When ready, recommends growing 20%/day, capped at the NEXT
    /// tier's own limit so the plan never recommends skipping a tier's ceiling in one step.
    /// </summary>
    public static TierAdvisorPlan ComputeSafeDailySendPlan(
        string currentTier, long recentAverageDailyVolume, string canonicalCurrentRating)
    {
        var currentLimit = DailyLimitFor(currentTier);
        var nextTier = NextTierMap.TryGetValue(currentTier, out var nt) ? nt : null;

        if (nextTier is null)
        {
            return new TierAdvisorPlan(
                currentTier, null, currentLimit, recentAverageDailyVolume, false,
                "Already at the highest tier (unlimited) — no further growth to plan for.");
        }

        if (canonicalCurrentRating != QualityCodes.Green)
        {
            return new TierAdvisorPlan(
                currentTier, nextTier, currentLimit, recentAverageDailyVolume, false,
                "Quality rating must be green before Guardian recommends growing send volume.");
        }

        var readyToGrow = !currentLimit.HasValue || recentAverageDailyVolume >= (long)(currentLimit.Value * 0.8);
        var recommendedDailyVolume = readyToGrow
            ? (long)(recentAverageDailyVolume * 1.2)
            : recentAverageDailyVolume;

        var nextLimit = DailyLimitFor(nextTier);
        if (nextLimit.HasValue)
        {
            recommendedDailyVolume = Math.Min(recommendedDailyVolume, nextLimit.Value);
        }

        var recommendation = readyToGrow
            ? $"Sustaining {recentAverageDailyVolume}/day at green quality — safe to grow toward ~{recommendedDailyVolume}/day."
            : "Sustain at least 80% of the current tier's daily limit at green quality before Guardian recommends growth.";

        return new TierAdvisorPlan(currentTier, nextTier, currentLimit, recommendedDailyVolume, readyToGrow, recommendation);
    }
}
