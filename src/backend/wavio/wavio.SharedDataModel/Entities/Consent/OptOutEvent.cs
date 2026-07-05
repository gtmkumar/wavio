namespace wavio.SharedDataModel.Entities.Consent;

/// <summary>
/// Append-only opt-out ledger row (consent.opt_out_events, db/migrations/V012__consent.sql,
/// issue #21, spec §4.10). The STOP-keyword listener writes one row here per detected inbound
/// opt-out, then the same unit of work upserts the (tenant, wa_id) marketing suppression into
/// <see cref="wavio.SharedDataModel.Entities.Messaging.SuppressionListEntry"/> — this table is
/// evidence, that table is enforcement (see the migration's own header comment).
/// </summary>
public class OptOutEvent
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>PII — mask in logs.</summary>
    public string WaId { get; set; } = null!;

    /// <summary>marketing | all (CHECK-enforced). Enforcement is uniform via the suppression
    /// list regardless of scope (spec §4.10: "immediate MARKETING suppression per (tenant,
    /// wa_id)") — see SuppressionListEntry's doc comment for why "all" doesn't get a stronger
    /// enforcement path in Wave 1.</summary>
    public string Scope { get; set; } = "marketing";

    /// <summary>stop_keyword | manual | complaint | bounce (CHECK-enforced).</summary>
    public string Reason { get; set; } = "stop_keyword";

    /// <summary>The matched keyword/phrase, verbatim, for a stop_keyword opt-out (e.g. "STOP",
    /// "बंद", "band karo"). Null for manual/complaint/bounce.</summary>
    public string? Keyword { get; set; }

    /// <summary>Detected language code for a stop_keyword opt-out (e.g. "en", "hi",
    /// "hi-Latn" for romanized Hindi). Null for manual/complaint/bounce.</summary>
    public string? Language { get; set; }

    /// <summary>Wamid of the inbound message that triggered a stop_keyword opt-out — also the
    /// idempotency key that keeps STOP-listener redelivery a no-op (no DB unique constraint on
    /// this column; the app checks before inserting).</summary>
    public string? InboundWamid { get; set; }

    /// <summary>Raw supporting payload (jsonb) — e.g. the full inbound message context for a
    /// manual/complaint entry raised from a support tool. Never log verbatim.</summary>
    public string? Payload { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}
