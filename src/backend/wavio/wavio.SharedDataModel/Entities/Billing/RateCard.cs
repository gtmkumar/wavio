namespace wavio.SharedDataModel.Entities.Billing;

/// <summary>
/// A versioned, effective-dated snapshot of Meta's rate card (billing.rate_cards,
/// db/migrations/V010__billing.sql; issue #19, spec §4.7). PLATFORM-GLOBAL reference data — no
/// tenant_id, no RLS (Meta's rate card is identical for every tenant on a given currency).
/// Future-dated cards can be loaded ahead of Meta's quarterly refresh (Jan/Apr/Jul/Oct 1); the
/// active card for "now" is the one with the greatest <see cref="EffectiveFrom"/> that is still
/// &lt;= today (see WaBilling.Application's RateCardSelector).
/// </summary>
public class RateCard
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string Currency { get; set; } = "INR";

    /// <summary>meta | manual (CHECK-enforced).</summary>
    public string Source { get; set; } = "meta";

    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }

    /// <summary>draft | active | superseded (CHECK-enforced).</summary>
    public string Status { get; set; } = "active";

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public int Version { get; set; }

    public List<RateCardEntry> Entries { get; set; } = [];
}
