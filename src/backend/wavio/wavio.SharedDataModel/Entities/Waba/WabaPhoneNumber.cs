namespace wavio.SharedDataModel.Entities.Waba;

/// <summary>
/// Read-mostly mapping of waba.phone_numbers (db/migrations/V002__waba.sql) — the internal GUID
/// ↔ Meta's raw <c>meta_phone_number_id</c> string bridge every service that talks to the Graph
/// API needs (issue #14's outbox dispatcher; issue #15's tenant resolver used a raw-SQL query
/// instead since it runs before a tenant is known — this entity is for callers that already have
/// a tenant context, like the dispatcher, which resolves it via its scoped tenant override).
/// Only the columns currently consumed anywhere are mapped; the table has more (business account,
/// verified name, etc.) not yet needed by any service.
/// <see cref="MessagingTier"/> was added by issue #19 — WaBilling's estimator reuses Meta's own
/// messaging-tier code (TIER_1K/TIER_10K/...) as the volume-tier key into
/// <c>billing.rate_card_entries.volume_tier</c>, rather than inventing separate tier thresholds.
/// </summary>
public class WabaPhoneNumber
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid BusinessAccountId { get; set; }
    public string MetaPhoneNumberId { get; set; } = null!;
    public string DisplayPhoneNumber { get; set; } = null!;
    public string Status { get; set; } = null!;

    /// <summary>Meta's own messaging-tier code (e.g. TIER_1K/TIER_10K/TIER_100K/TIER_UNLIMITED),
    /// null until Meta reports one. Issue #19: doubles as the volume-tier key for utility/auth
    /// rate-card lookups (marketing never uses a tier — spec §4.7, no volume discounts).</summary>
    public string? MessagingTier { get; set; }

    /// <summary>Meta's current quality rating for this number — GREEN/YELLOW/RED/UNKNOWN
    /// (uppercase; db/migrations/V002's CHECK constraint, note this is a DIFFERENT casing than
    /// quality.number_quality_events.new_rating, which is lowercase — see
    /// WaIntel.Application.Quality.Logic.QualityCodes for the two-way mapping). Added by issue #20
    /// (Quality Rating Guardian, spec §4.6) — the column already existed since V002 but was unused
    /// until now.</summary>
    public string? QualityRating { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; }
}
