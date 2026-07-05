namespace wavio.SharedDataModel.Entities.Messaging;

/// <summary>
/// Per-tenant do-not-send registry (messaging.suppression_list, db/migrations/V007__messaging.sql
/// — the table predates this entity; it went unmapped until issue #21 gave it a writer/reader).
/// Enforced by the gateway pre-dispatch, deny-wins (spec §4.10): a MARKETING send to a
/// suppressed (tenant, wa_id) is rejected before it ever reaches the outbox. The table has no
/// scope column, so a row here means "no marketing" specifically — utility/authentication/service
/// sends are never blocked by this list (spec §4.10's own wording: "immediate MARKETING
/// suppression"). A consent.opt_out_events row with scope='all' still only produces ONE
/// suppression_list row (the schema has nowhere else to record "all"); this is a documented Wave
/// 1 gap, not a silent one — see consent.OptOutEvent's Scope doc comment.
/// </summary>
public class SuppressionListEntry
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>PII — mask in logs.</summary>
    public string WaId { get; set; } = null!;

    /// <summary>opt_out | stop_keyword | hard_error | complaint | compliance | manual
    /// (CHECK-enforced).</summary>
    public string Reason { get; set; } = null!;

    public string? Source { get; set; }
    public string? Notes { get; set; }

    /// <summary>Null = permanent suppression.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
}
