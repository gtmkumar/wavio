using wavio.SharedDataModel.Entities.Billing;

namespace WaBilling.Application.RateCards.Logic;

/// <summary>
/// Pure rate-card and rate-card-entry selection (issue #19, spec §4.7). No I/O — callers load the
/// candidate cards/entries (already filtered to the currency of interest) and pass them in, so
/// this is fully unit-testable without a database.
/// </summary>
public static class RateCardSelector
{
    /// <summary>
    /// Picks the active card for "now": the greatest <see cref="RateCard.EffectiveFrom"/> that is
    /// still &lt;= <paramref name="asOf"/> and (if set) not yet past its
    /// <see cref="RateCard.EffectiveTo"/>. Future-dated cards (effective_from in the future) are
    /// simply not candidates yet — they become the active card the instant "now" reaches their
    /// effective_from, with no code change needed (spec §4.7: "future-dated rates loadable in
    /// advance"). <paramref name="cards"/> should already be filtered to one currency.
    /// </summary>
    public static RateCard? SelectActiveCard(IEnumerable<RateCard> cards, DateOnly asOf) =>
        cards
            .Where(c => c.EffectiveFrom <= asOf && (c.EffectiveTo is null || c.EffectiveTo >= asOf))
            .OrderByDescending(c => c.EffectiveFrom)
            .FirstOrDefault();

    /// <summary>
    /// Picks the priced entry for (category, market), preferring an exact
    /// <paramref name="volumeTier"/> match and falling back to the tier-agnostic (null-tier) row
    /// when no tier-specific entry exists for this card. Marketing has no volume discounts (spec
    /// §4.7) — callers should pass <paramref name="volumeTier"/> = null for marketing so only the
    /// tier-agnostic row is ever considered.
    /// </summary>
    public static RateCardEntry? SelectEntry(
        IEnumerable<RateCardEntry> entries, string category, string market, string? volumeTier)
    {
        var candidates = entries
            .Where(e => string.Equals(e.Category, category, StringComparison.OrdinalIgnoreCase)
                     && string.Equals(e.Market, market, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (volumeTier is not null)
        {
            var exact = candidates.FirstOrDefault(e =>
                string.Equals(e.VolumeTier, volumeTier, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact;
        }

        return candidates.FirstOrDefault(e => e.VolumeTier is null);
    }
}
