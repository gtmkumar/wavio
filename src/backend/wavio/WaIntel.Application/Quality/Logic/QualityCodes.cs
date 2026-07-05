namespace WaIntel.Application.Quality.Logic;

/// <summary>
/// Casing/vocabulary bridge between the three different representations of "quality rating" and
/// "messaging tier" that already exist in the schema (issue #20). No I/O — pure string mapping,
/// fully unit-testable.
///
/// SCHEMA QUIRK (pre-existing, not introduced here — migrations are frozen, no schema changes):
///   • <c>waba.phone_numbers.quality_rating</c> (db/migrations/V002) CHECKs UPPERCASE:
///     GREEN | YELLOW | RED | UNKNOWN.
///   • <c>quality.number_quality_events.new_rating</c> / <c>guardian_incidents.trigger_rating</c>
///     (db/migrations/V011) CHECK lowercase: green | yellow | red | unknown.
/// Both must be written correctly per their own CHECK, so this class provides the one place that
/// converts between them rather than scattering case-conversion across handlers.
///
///   • <c>waba.phone_numbers.messaging_tier</c> stores Meta's own raw code verbatim (e.g.
///     TIER_1K — issue #19, no CHECK constraint, deliberately open-ended for whatever Meta sends).
///   • <c>quality.messaging_tier_events.new_tier</c> (V011) CHECKs a canonical lowercase set:
///     tier_250 | tier_1k | tier_10k | tier_100k | tier_unlimited.
/// <see cref="TryNormalizeTier"/> maps the former to the latter; an unrecognized raw tier code
/// returns false so the caller can still store the raw value on <c>waba.phone_numbers</c> (matching
/// issue #19's "store whatever Meta sends" convention) while skipping the CHECK-constrained event
/// row rather than risk an insert failure.
/// </summary>
public static class QualityCodes
{
    public const string Green = "green";
    public const string Yellow = "yellow";
    public const string Red = "red";
    public const string Unknown = "unknown";

    /// <summary>Maps any casing/variant of Meta's rating text (e.g. "GREEN", "green_quality") to
    /// the platform's canonical lowercase form. Unrecognized input maps to <see cref="Unknown"/>
    /// rather than throwing — a webhook field Meta changes the wording of should degrade
    /// gracefully, not break ingestion.</summary>
    public static string NormalizeRating(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Unknown;
        var upper = raw.Trim().ToUpperInvariant();
        if (upper.Contains("GREEN", StringComparison.Ordinal)) return Green;
        if (upper.Contains("YELLOW", StringComparison.Ordinal)) return Yellow;
        if (upper.Contains("RED", StringComparison.Ordinal)) return Red;
        return Unknown;
    }

    /// <summary>The uppercase form <c>waba.phone_numbers.quality_rating</c>'s CHECK requires,
    /// from a canonical lowercase rating produced by <see cref="NormalizeRating"/>.</summary>
    public static string ToPhoneNumberRatingColumn(string canonicalLowerRating) =>
        canonicalLowerRating.ToUpperInvariant();

    private static readonly Dictionary<string, string> RawTierToCanonical = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TIER_250"] = "tier_250",
        ["TIER_1K"] = "tier_1k",
        ["TIER_10K"] = "tier_10k",
        ["TIER_100K"] = "tier_100k",
        ["TIER_UNLIMITED"] = "tier_unlimited",
    };

    /// <summary>True and sets <paramref name="canonicalTier"/> when Meta's raw tier code maps to
    /// one of the platform's canonical (CHECK-constrained) tier codes; false for anything else
    /// (e.g. a tier code Meta introduces that the platform doesn't recognize yet).</summary>
    public static bool TryNormalizeTier(string? rawMetaTier, out string canonicalTier)
    {
        if (rawMetaTier is not null && RawTierToCanonical.TryGetValue(rawMetaTier, out var canonical))
        {
            canonicalTier = canonical;
            return true;
        }
        canonicalTier = string.Empty;
        return false;
    }
}
