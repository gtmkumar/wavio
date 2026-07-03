namespace wavio.SharedDataModel.Entities.Sessions;

/// <summary>
/// Append-only history of window transitions (sessions.window_events, issue #15) — debug/audit
/// trail and the mechanism QA uses to verify CS/CTWA scenarios played out correctly, independent
/// of the current-state row in <see cref="ConversationWindow"/>.
/// </summary>
public class WindowEvent
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ConversationWindowId { get; set; }

    /// <summary>cs_opened | cs_extended | cs_expired | ctwa_opened | ctwa_extended | ctwa_expired |
    /// closing_notified | simulated (CHECK-enforced in the DB).</summary>
    public string EventType { get; set; } = null!;

    public DateTimeOffset? OldExpiresAt { get; set; }
    public DateTimeOffset? NewExpiresAt { get; set; }

    /// <summary>Free-form context (jsonb) — e.g. the inbound wamid that triggered this transition.</summary>
    public string? Payload { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}
