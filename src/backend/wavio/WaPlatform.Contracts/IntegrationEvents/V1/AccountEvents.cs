namespace WaPlatform.Contracts.IntegrationEvents.V1;

/// <summary>
/// <c>wa.quality.changed.v1</c> — Meta changed a phone number's quality rating or messaging
/// tier; consumed by verticals and by the Guardian's auto-throttle (spec §4.6).
/// </summary>
public sealed record QualityChangedV1 : IntegrationEvent
{
    public const string Name = "wa.quality.changed.v1";
    public override string EventName => Name;

    public required string PhoneNumberId { get; init; }
    public required string WabaId { get; init; }

    /// <summary>green | yellow | red (Meta quality rating).</summary>
    public required string PreviousRating { get; init; }
    public required string CurrentRating { get; init; }

    /// <summary>Messaging tier after the change (e.g. TIER_1K, TIER_10K, TIER_100K, TIER_UNLIMITED).</summary>
    public required string MessagingTier { get; init; }

    /// <summary>True when the Guardian applied/changed an automatic send throttle in response.</summary>
    public bool AutoThrottleApplied { get; init; }
}

/// <summary>
/// <c>wa.tier.changed.v1</c> — Meta changed a phone number's messaging tier (the
/// marketing-initiated unique-users/24h ceiling: 250 → 1K → 10K → 100K → unlimited),
/// reported alongside (or independently of) a quality-rating change (spec §4.2, §4.6
/// tier-growth advisor). Kept distinct from <see cref="QualityChangedV1"/> so consumers
/// that only care about throughput headroom don't have to parse quality semantics.
/// </summary>
public sealed record TierChangedV1 : IntegrationEvent
{
    public const string Name = "wa.tier.changed.v1";
    public override string EventName => Name;

    public required string PhoneNumberId { get; init; }
    public required string WabaId { get; init; }

    /// <summary>
    /// Tier before the change when known. Wave 1 has no persisted quality/tier snapshot
    /// (that lands with the Wave 2 <c>quality</c> schema, issue #20) — the ingest layer
    /// cannot yet diff against a prior value, so this is <c>null</c> until then.
    /// </summary>
    public string? PreviousTier { get; init; }

    /// <summary>e.g. TIER_250, TIER_1K, TIER_10K, TIER_100K, TIER_UNLIMITED.</summary>
    public required string NewTier { get; init; }
}

/// <summary>
/// <c>wa.account.alert.v1</c> — WABA-level alert requiring attention: policy violation,
/// account restriction, INR-billing migration deadline, rate-card change, … (spec §4.1, §13).
/// </summary>
public sealed record AccountAlertV1 : IntegrationEvent
{
    public const string Name = "wa.account.alert.v1";
    public override string EventName => Name;

    public required string WabaId { get; init; }

    /// <summary>Machine-readable alert type (e.g. policy_violation, inr_migration_due, rate_card_updated).</summary>
    public required string AlertType { get; init; }

    /// <summary>info | warning | critical.</summary>
    public required string Severity { get; init; }

    /// <summary>Human-readable detail for dashboards/notifications.</summary>
    public required string Detail { get; init; }
}
