namespace wavio.SharedDataModel.Entities.Messaging;

/// <summary>
/// One row per accepted <c>POST /v1/messages</c> request (messaging.outbound_messages, issue
/// #14, db/migrations/V007__messaging.sql). <see cref="IdempotencyKey"/> is unique per tenant
/// while <see cref="IdempotencyActive"/> is true (partial unique index) — a duplicate key within
/// the 24h window hits that constraint; the gateway catches the violation and returns the
/// original row's result rather than erroring.
/// </summary>
public class OutboundMessage
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PhoneNumberId { get; set; }

    /// <summary>Recipient WhatsApp id — PII, mask in logs.</summary>
    public string ToWaId { get; set; } = null!;

    /// <summary>text | template | media | interactive_buttons | interactive_list |
    /// interactive_cta_url | interactive_flow | location | contacts | reaction | order_details.</summary>
    public string MessageType { get; set; } = null!;

    public Guid? TemplateId { get; set; }
    public Guid? TemplateVersionId { get; set; }

    /// <summary>The type-specific payload as submitted (jsonb) — retained for audit/replay.</summary>
    public string Payload { get; set; } = null!;

    public string IdempotencyKey { get; set; } = null!;

    /// <summary>Cleared to false by the 24h idempotency-window job once accepted_at is stale;
    /// see the migration header comment — a partial unique index can't reference now().</summary>
    public bool IdempotencyActive { get; set; } = true;

    /// <summary>accepted -> dispatched (Graph accepted, wamid set) -> sent -> delivered -> read,
    /// or failed/rejected (rejected = pre-dispatch policy rejection: WINDOW_CLOSED, suppression,
    /// tier exhausted — never reaches the outbox).</summary>
    public string Status { get; set; } = "accepted";

    /// <summary>Meta's message id — set only once Graph accepts the send.</summary>
    public string? Wamid { get; set; }

    /// <summary>Set for marketing template sends (spec §4.2) — true means Meta will bill this
    /// send; null for message types where billability isn't estimated pre-send.</summary>
    public bool? BillableEstimate { get; set; }

    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    public DateTimeOffset AcceptedAt { get; set; }
    public DateTimeOffset? DispatchedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public int Version { get; set; }
}
