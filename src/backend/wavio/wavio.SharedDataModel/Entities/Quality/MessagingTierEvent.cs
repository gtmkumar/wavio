namespace wavio.SharedDataModel.Entities.Quality;

/// <summary>
/// Append-only log of a phone number's messaging-tier transitions
/// (quality.messaging_tier_events, issue #20, db/migrations/V011__quality.sql). Tier codes here
/// are the platform's CANONICAL lowercase form (tier_250/tier_1k/tier_10k/tier_100k/
/// tier_unlimited, CHECK-enforced) — distinct from <c>waba.phone_numbers.messaging_tier</c>,
/// which stores Meta's own raw code verbatim (e.g. TIER_1K, issue #19). See
/// <c>WaIntel.Application.Quality.Logic.QualityCodes</c> for the mapping between the two.
/// </summary>
public class MessagingTierEvent
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PhoneNumberId { get; set; }

    public string? OldTier { get; set; }
    public string NewTier { get; set; } = null!;

    /// <summary>webhook | manual | simulated (CHECK-enforced).</summary>
    public string EventSource { get; set; } = "webhook";

    public string? Payload { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}
