namespace wavio.SharedDataModel.Entities.Consent;

/// <summary>
/// DPDP data-principal rights workflow row (consent.erasure_requests,
/// db/migrations/V012__consent.sql, issue #21, spec §4.10/§9). Erasure blanks message content
/// for the (tenant, wa_id) but deliberately PRESERVES billing.message_costs (8-year tax
/// retention, spec §4.10) and this ledger's own consent evidence
/// (<see cref="OptInEvent"/>/<see cref="OptOutEvent"/> rows are never touched by an erasure —
/// they ARE the compliance record of what consent existed and when it ended).
/// </summary>
public class ErasureRequest
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>PII — mask in logs.</summary>
    public string WaId { get; set; } = null!;

    /// <summary>erasure | export (CHECK-enforced).</summary>
    public string RequestType { get; set; } = "erasure";

    /// <summary>pending | processing | completed | rejected (CHECK-enforced).</summary>
    public string Status { get; set; } = "pending";

    /// <summary>Free text: who raised the request (support agent, self-service portal
    /// identifier). Not a platform user FK.</summary>
    public string? RequestedBy { get; set; }

    public string? Reason { get; set; }

    /// <summary>Optional scope narrowing (jsonb) — e.g. a date range. Null means "everything
    /// this wa_id has" within the request type's own bounds (see the worker's doc comment for
    /// exactly what "everything" means for erasure vs. export).</summary>
    public string? Scope { get; set; }

    /// <summary>Set the moment message CONTENT (not cost-ledger metadata) is blanked — erasure
    /// only. Null for a pending/rejected request or for an export request (exports never erase).</summary>
    public DateTimeOffset? ContentErasedAt { get; set; }

    /// <summary>Export requests only: where the collected export payload was written (v1: a
    /// local file path under a configured export directory — see
    /// WaAdmin.Infrastructure.BackgroundWork.ErasureRequestProcessorService's doc comment for why
    /// this is pragmatic rather than an object-storage URL in Wave 1).</summary>
    public string? ExportRef { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public int Version { get; set; }
}
