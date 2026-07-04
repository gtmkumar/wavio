namespace WaPlatform.Contracts.IntegrationEvents.V1;

/// <summary>
/// <c>wa.flow.response.v1</c> — a customer completed (or abandoned with data) a WhatsApp Flow;
/// decrypted response payload routed to the owning vertical (spec §4.8).
/// </summary>
public sealed record FlowResponseV1 : IntegrationEvent
{
    public const string Name = "wa.flow.response.v1";
    public override string EventName => Name;

    public required string FlowId { get; init; }

    /// <summary>Wamid of the flow completion message.</summary>
    public required string Wamid { get; init; }

    /// <summary>Customer WhatsApp id (PII — mask in logs).</summary>
    public required string WaId { get; init; }

    /// <summary>Decrypted flow response as a JSON document string (schema owned by the flow).</summary>
    public required string ResponseJson { get; init; }
}

/// <summary>
/// <c>wa.payment.status.v1</c> — status change of a UPI payment initiated via
/// <c>order_details</c> (spec §4.9): pending → success | failed | expired.
/// </summary>
public sealed record PaymentStatusV1 : IntegrationEvent
{
    public const string Name = "wa.payment.status.v1";
    public override string EventName => Name;

    /// <summary>Platform payment order id (reference_id sent in order_details).</summary>
    public required string ReferenceId { get; init; }

    /// <summary>pending | success | failed | expired | refunded.</summary>
    public required string Status { get; init; }

    /// <summary>Amount in the smallest currency unit (paise); INR only in v1.</summary>
    public required long AmountMinorUnits { get; init; }

    public string Currency { get; init; } = "INR";

    /// <summary>PSP transaction id when available (reconciliation key).</summary>
    public string? PspTransactionId { get; init; }
}

/// <summary>
/// <c>wa.template.status_changed.v1</c> — Meta moved a template through its review
/// state machine (spec §4.4): pending → approved | rejected | paused | disabled.
/// </summary>
public sealed record TemplateStatusChangedV1 : IntegrationEvent
{
    public const string Name = "wa.template.status_changed.v1";
    public override string EventName => Name;

    /// <summary>Platform template id.</summary>
    public required Guid TemplateId { get; init; }

    /// <summary>Meta-side template id.</summary>
    public required string MetaTemplateId { get; init; }

    public required string PreviousStatus { get; init; }
    public required string NewStatus { get; init; }

    /// <summary>Meta rejection/pause reason when present.</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// <c>wa.template.category_changed.v1</c> — Meta recategorized a template (e.g.
/// utility → marketing), which changes its per-message cost (spec §4.4). Tenant
/// notification + billing recalibration are mandatory reactions to this event.
/// </summary>
public sealed record TemplateCategoryChangedV1 : IntegrationEvent
{
    public const string Name = "wa.template.category_changed.v1";
    public override string EventName => Name;

    /// <summary>Platform template id.</summary>
    public required Guid TemplateId { get; init; }

    /// <summary>Meta-side template id.</summary>
    public required string MetaTemplateId { get; init; }

    /// <summary>utility | marketing | authentication.</summary>
    public required string PreviousCategory { get; init; }
    public required string NewCategory { get; init; }
}
