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
