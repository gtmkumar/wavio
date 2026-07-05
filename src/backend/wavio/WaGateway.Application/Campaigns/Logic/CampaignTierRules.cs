using WaGateway.Application.Messages.Logic;

namespace WaGateway.Application.Campaigns.Logic;

/// <summary>
/// Pure tier-headroom chunking rules for the campaign engine (issue #22, spec §4.2: "Messaging
/// limits (marketing-initiated unique users / 24h) ... enforced pre-dispatch; campaign engine
/// chunks broadcasts to fit tier headroom"). No I/O — the chunker
/// (<c>CampaignChunkerService</c>) resolves the phone number's raw tier code, the last-24h
/// consumed count, and the open Guardian incident's throttle action, then passes them in here.
///
/// A local copy of the tier vocabulary/limits rather than a shared reference to WaIntel's
/// <c>TierRules</c> (issue #20) — same cross-service duplication convention already established
/// for <c>GuardianThrottleRules</c>/<c>ITenantResolver</c> (each service owns its own copy of the
/// small amount of shared vocabulary rather than taking a project reference across the
/// bounded-context boundary). Deliberately keyed on Meta's RAW tier code (e.g. <c>TIER_1K</c>, as
/// stored verbatim on <c>waba.phone_numbers.messaging_tier</c>, issue #19 convention) rather than
/// WaIntel's canonical lowercase form — this class never touches <c>quality.*</c> tables, only the
/// phone number's own tier column.
/// </summary>
public static class CampaignTierRules
{
    private const string TierUnlimitedRaw = "TIER_UNLIMITED";

    private static readonly IReadOnlyDictionary<string, int> RawDailyLimit =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["TIER_250"] = 250,
            ["TIER_1K"] = 1_000,
            ["TIER_10K"] = 10_000,
            ["TIER_100K"] = 100_000,
        };

    /// <summary>Numeric daily headroom ceiling for Meta's raw tier code; null means unlimited
    /// (<c>TIER_UNLIMITED</c> — no ceiling to enforce). A null/blank/unrecognized tier (Meta hasn't
    /// reported one yet, or reports a code this platform doesn't recognize) resolves to the most
    /// conservative KNOWN tier (250/day) rather than unlimited — fail-closed, same "never guess
    /// generous when we can't confirm" convention as ADR-005's window-state fallback.</summary>
    public static int? DailyLimitForRawTier(string? rawTier)
    {
        if (string.IsNullOrWhiteSpace(rawTier))
        {
            return RawDailyLimit["TIER_250"];
        }
        if (string.Equals(rawTier, TierUnlimitedRaw, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return RawDailyLimit.TryGetValue(rawTier, out var limit) ? limit : RawDailyLimit["TIER_250"];
    }

    /// <summary>Remaining daily-volume headroom: the tier ceiling minus what's already been
    /// consumed in the trailing 24h. <see cref="int.MaxValue"/> stands in for "unlimited" so
    /// callers can treat this uniformly with a per-tick pending-count cap via <see cref="Math.Min"/>.</summary>
    public static int ComputeHeadroom(int? dailyLimit, int consumedInLast24h) =>
        dailyLimit is null ? int.MaxValue : Math.Max(0, dailyLimit.Value - consumedInLast24h);

    /// <summary>Guardian YELLOW ("marketing_50pct") halves the PER-TICK CHUNK — how many new
    /// recipients this tick claims — not the tier's own daily ceiling (spec §4.6: "cut to 50%
    /// velocity"; Meta's own tier limit is unaffected, Guardian only slows how fast the campaign
    /// consumes it). Integer division, floored at zero by <see cref="ComputeHeadroom"/> already
    /// having floored the input.</summary>
    public static int ApplyGuardianThrottle(int headroom, string? throttleAction) =>
        GuardianThrottleRules.IsHalved(throttleAction) ? headroom / 2 : headroom;

    /// <summary>
    /// The number of 'pending' recipients to claim this tick. Zero when Guardian has frozen
    /// marketing sends for this number (<see cref="GuardianThrottleRules.IsFrozen"/>) — the
    /// campaign is skipped entirely this tick, recipients stay pending, and the NEXT tick
    /// (possibly the next day, once the incident resolves) picks up where this one left off. Never
    /// exceeds <paramref name="pendingCount"/> — no point claiming more than actually exists.
    /// </summary>
    public static int ComputeChunkSize(
        int? dailyLimit, int consumedInLast24h, string? throttleAction, int pendingCount)
    {
        if (GuardianThrottleRules.IsFrozen(throttleAction))
        {
            return 0;
        }
        var headroom = ComputeHeadroom(dailyLimit, consumedInLast24h);
        var afterThrottle = ApplyGuardianThrottle(headroom, throttleAction);
        return Math.Min(afterThrottle, pendingCount);
    }
}
