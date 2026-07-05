namespace wavio.SharedDataModel.Entities.Messaging;

/// <summary>
/// One row per <c>POST /v1/campaigns</c> broadcast (messaging.campaigns, issue #22,
/// db/migrations/V013__campaigns.sql, spec §4.2/§7.1). Pins an immutable
/// <see cref="TemplateVersionId"/> (spec §4.4: templates immutable post-approval; campaigns pin
/// versions). Counters are rollups derived from <see cref="CampaignRecipient"/> status
/// transitions — never incremented independently of a recipient's own transition.
/// </summary>
public class Campaign
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PhoneNumberId { get; set; }
    public string Name { get; set; } = null!;
    public Guid TemplateVersionId { get; set; }

    /// <summary>Campaign-level default template variable/component values (jsonb) — a per-recipient
    /// <see cref="CampaignRecipient.Params"/> value overrides this when present. Same "opaque,
    /// caller-supplied Meta component JSON" shape as <c>TemplatePayload.ComponentsJson</c> (issue
    /// #14) — no template-variable substitution engine is built here.</summary>
    public string? Params { get; set; }

    /// <summary>draft -&gt; scheduled -&gt; running -&gt; completed, with paused (Guardian
    /// throttle / template pause — see <see cref="CampaignRecipient"/>'s doc comment), cancelled
    /// (terminal, remaining pending recipients marked cancelled) and failed (terminal — launch-level
    /// error, or the pinned template was DISABLED mid-flight).</summary>
    public string Status { get; set; } = "draft";

    public DateTimeOffset? ScheduledAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public int AudienceCount { get; set; }
    public int SuppressedCount { get; set; }
    public int SentCount { get; set; }
    public int DeliveredCount { get; set; }
    public int ReadCount { get; set; }
    public int FailedCount { get; set; }

    /// <summary>Pre-launch estimate from wa-billing-svc's estimator (spec §4.7) — advisory only,
    /// same "estimates are advisory, the webhook pricing object is the billing source of truth"
    /// caveat as every other estimator consumer. Null when the estimator was unreachable or had no
    /// priced rate-card entry at creation time (never a silently-wrong zero).</summary>
    public decimal? ProjectedCost { get; set; }
    public string? ProjectedCurrency { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public int Version { get; set; }
}
