namespace wavio.SharedDataModel.Entities.Billing;

/// <summary>
/// One price for (category x market x volume tier) within a <see cref="RateCard"/>
/// (billing.rate_card_entries, issue #19, spec §4.7). <see cref="VolumeTier"/> null means
/// tier-agnostic — marketing has no volume discounts (spec §4.7) and always uses the
/// tier-agnostic row; utility/authentication track Meta's own messaging-tier discounts, keyed
/// by the same tier codes as <c>waba.phone_numbers.messaging_tier</c> (e.g. TIER_1K, TIER_10K).
/// PLATFORM-GLOBAL — no tenant_id, no RLS (same posture as <see cref="RateCard"/>).
/// </summary>
public class RateCardEntry
{
    public Guid Id { get; set; }
    public Guid RateCardId { get; set; }

    /// <summary>marketing | utility | authentication | authentication_international | service.</summary>
    public string Category { get; set; } = null!;

    public string Market { get; set; } = null!;
    public string? VolumeTier { get; set; }
    public decimal PricePerMessage { get; set; }
    public string Currency { get; set; } = "INR";

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public int Version { get; set; }
}
