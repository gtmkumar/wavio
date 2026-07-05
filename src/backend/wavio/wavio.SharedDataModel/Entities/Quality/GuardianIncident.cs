namespace wavio.SharedDataModel.Entities.Quality;

/// <summary>
/// A Guardian auto-throttle incident (quality.guardian_incidents, issue #20, spec §4.6,
/// db/migrations/V011__quality.sql). Mutable current-state row (unlike the append-only
/// <c>*_events</c> tables) — same shape as
/// <see cref="wavio.SharedDataModel.Entities.Sessions.ConversationWindow"/>: Version/UpdatedAt for
/// optimistic concurrency. WaGateway's outbox dispatcher reads the newest non-resolved row per
/// phone number directly (see OutboxDispatcherService) to decide whether to throttle/freeze a
/// marketing send — this is the single source of truth for that decision.
/// </summary>
public class GuardianIncident
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PhoneNumberId { get; set; }

    /// <summary>quality_yellow | quality_red | tier_downgrade | block_rate_spike |
    /// template_paused (CHECK-enforced).</summary>
    public string IncidentType { get; set; } = null!;

    /// <summary>info | warning | critical (CHECK-enforced).</summary>
    public string Severity { get; set; } = "warning";

    /// <summary>open | mitigating | resolved (CHECK-enforced).</summary>
    public string Status { get; set; } = "open";

    /// <summary>none | marketing_50pct | marketing_frozen (CHECK-enforced) — what WaGateway must
    /// enforce pre-dispatch for marketing sends on this number while the incident is open.</summary>
    public string ThrottleAction { get; set; } = "none";

    /// <summary>The rating that triggered this incident (lowercase), when applicable.</summary>
    public string? TriggerRating { get; set; }

    /// <summary>Free-form context (jsonb) — e.g. the raw webhook payload or computed metrics.</summary>
    public string? Details { get; set; }

    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public int Version { get; set; }
}
