namespace WaPlatform.Contracts.Dtos.Messaging;

/// <summary>
/// Request body for <c>POST /v1/messages</c> (spec §7.1) — the single outbound send API on
/// wa-gateway-svc for all message types. Stub for #8; hardened by Wave 1 #14
/// (idempotency, outbox, window-aware rejection per ADR-005).
/// </summary>
public sealed record SendMessageRequest
{
    /// <summary>Customer WhatsApp id to deliver to (PII — mask in logs).</summary>
    public required string To { get; init; }

    /// <summary>Sending phone number (Meta phone_number_id).</summary>
    public required string PhoneNumberId { get; init; }

    /// <summary>text | template | image | document | interactive | flow | order_details | …</summary>
    public required string MessageType { get; init; }

    /// <summary>
    /// Type-specific payload as a JSON document string (Graph API message object shape).
    /// Typed payload records land with the Wave 1 send slices.
    /// </summary>
    public required string PayloadJson { get; init; }

    /// <summary>
    /// Caller-supplied idempotency key — replays return the original accepted response
    /// instead of double-sending.
    /// </summary>
    public required string IdempotencyKey { get; init; }
}

/// <summary>Accepted-send response for <c>POST /v1/messages</c>.</summary>
public sealed record SendMessageResponse
{
    /// <summary>WhatsApp message id assigned by Meta; correlation id for the status chain.</summary>
    public required string Wamid { get; init; }

    /// <summary>accepted | rejected_window_closed | rejected_quota | rejected_suppressed | throttled.</summary>
    public required string Status { get; init; }
}

/// <summary>
/// Window state for <c>GET /v1/windows/{waId}</c> (Session Window Manager, spec §4.5).
/// </summary>
public sealed record WindowStateDto
{
    /// <summary>Customer WhatsApp id (PII — mask in logs).</summary>
    public required string WaId { get; init; }

    public required string PhoneNumberId { get; init; }

    /// <summary>True when a customer-service or CTWA window is currently open.</summary>
    public required bool IsOpen { get; init; }

    /// <summary>customer_service (24h) | ctwa (72h) | none.</summary>
    public required string WindowType { get; init; }

    /// <summary>UTC expiry of the open window; null when closed.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }
}
