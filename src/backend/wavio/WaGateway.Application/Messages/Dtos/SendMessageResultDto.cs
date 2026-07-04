namespace WaGateway.Application.Messages.Dtos;

/// <summary>
/// Result of <c>POST /v1/messages</c> — covers all three outcomes with one shape so the endpoint
/// can map to HTTP status from <see cref="Status"/> alone: "accepted" → 200, "rejected" → 422
/// with <see cref="ErrorCode"/> (e.g. WINDOW_CLOSED). A retried request with the same
/// Idempotency-Key returns this SAME shape for whichever outcome the original request had —
/// including a rejection, so a duplicate of a rejected send is rejected again, consistently.
/// </summary>
public sealed record SendMessageResultDto(
    Guid Id,
    string Status,
    string? Wamid,
    bool? BillableEstimate,
    string? ErrorCode,
    string? ErrorMessage);
