namespace WaIntel.Application.Quality.Logic;

/// <summary>
/// Pure Guardian auto-throttle decision rules (issue #20, spec §4.6). No I/O — callers load
/// whatever rows they need and pass in plain values, so every rule here is unit-testable without a
/// database. Mirrors WaBilling's <c>QuotaRules</c> in shape (issue #19): a static rules class next
/// to the handler that applies it.
/// </summary>
public static class GuardianRules
{
    public const string IncidentQualityYellow = "quality_yellow";
    public const string IncidentQualityRed = "quality_red";
    public const string IncidentTierDowngrade = "tier_downgrade";

    public const string ThrottleNone = "none";
    public const string Throttle50Pct = "marketing_50pct";
    public const string ThrottleFrozen = "marketing_frozen";

    public const string StatusOpen = "open";
    public const string StatusResolved = "resolved";

    /// <summary>The two quality-driven incident types that are mutually exclusive per phone
    /// number — a number is either yellow-throttled or red-frozen, never both at once.</summary>
    public static readonly IReadOnlyList<string> QualityIncidentTypes = [IncidentQualityYellow, IncidentQualityRed];

    /// <summary>YELLOW → open a quality_yellow incident; RED → quality_red. GREEN/UNKNOWN never
    /// open a new quality incident (spec §4.6: only degraded ratings trigger throttling).</summary>
    public static string? DetermineIncidentType(string canonicalNewRating) => canonicalNewRating switch
    {
        QualityCodes.Yellow => IncidentQualityYellow,
        QualityCodes.Red => IncidentQualityRed,
        _ => null,
    };

    /// <summary>Spec §4.6's exact auto-throttle policy: YELLOW cuts marketing to 50% velocity, RED
    /// freezes marketing entirely. Tier downgrades are informational only in v1 — no throttle.</summary>
    public static string DetermineThrottleAction(string incidentType) => incidentType switch
    {
        IncidentQualityYellow => Throttle50Pct,
        IncidentQualityRed => ThrottleFrozen,
        _ => ThrottleNone,
    };

    public static string DetermineSeverity(string incidentType) => incidentType switch
    {
        IncidentQualityRed => "critical",
        IncidentQualityYellow => "warning",
        IncidentTierDowngrade => "info",
        _ => "warning",
    };

    /// <summary>A recovery to GREEN resolves any open quality incident (spec §4.6: "recovery to
    /// GREEN → resolve open incidents"). UNKNOWN is not a recovery — it's a loss of signal, not
    /// confirmation the number is healthy again.</summary>
    public static bool ShouldResolveOnRecovery(string canonicalNewRating) =>
        canonicalNewRating == QualityCodes.Green;
}
