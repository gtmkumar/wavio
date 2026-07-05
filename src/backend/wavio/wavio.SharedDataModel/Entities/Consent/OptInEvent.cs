using System.Net;

namespace wavio.SharedDataModel.Entities.Consent;

/// <summary>
/// Append-only opt-in evidence row (consent.opt_in_events, db/migrations/V012__consent.sql,
/// issue #21, spec §4.10). One row per capture event — never updated, never deleted. The
/// current consent state for a (tenant, wa_id, purpose) is derived by reading the latest row
/// here and checking it against <see cref="OptOutEvent"/>/suppression, not by mutating a single
/// "current" row.
/// </summary>
public class OptInEvent
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>The consenting/service-recipient WhatsApp id — PII, mask in logs. When the
    /// consenting party differs from the service recipient (behalf-of consent, spec §4.10), the
    /// recipient on whose behalf consent was captured is recorded inside <see cref="Evidence"/>
    /// (there is no separate on-behalf-of column — the schema is frozen, V012).</summary>
    public string WaId { get; set; } = null!;

    /// <summary>transactional | marketing | service (CHECK-enforced).</summary>
    public string Purpose { get; set; } = null!;

    /// <summary>web_form | qr | in_chat | in_person | api | import (CHECK-enforced).</summary>
    public string CaptureChannel { get; set; } = null!;

    /// <summary>Evidence blob (jsonb): form payload hash, uploaded proof ref, and — for a
    /// behalf-of capture — the explicit on-behalf-of fields (see <see cref="WaId"/>'s doc
    /// comment). Never log this verbatim (may carry PII/consent proof).</summary>
    public string? Evidence { get; set; }

    /// <summary>Wamid of the inbound message that constituted/confirmed consent, when the
    /// evidence is a WhatsApp reply rather than an external form/QR capture.</summary>
    public string? EvidenceWamid { get; set; }

    /// <summary>Free text: who/what captured this consent (staff member, kiosk id, web form
    /// name). Not a platform user FK — <see cref="CreatedBy"/> captures the platform actor when
    /// there is one.</summary>
    public string? Actor { get; set; }

    public IPAddress? SourceIp { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}
