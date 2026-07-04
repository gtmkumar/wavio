namespace WaGateway.Application.Common.Interfaces;

/// <summary>Everything the outbox dispatcher needs to attempt one Graph send.</summary>
public sealed record GraphSendRequest(string MetaPhoneNumberId, string ToWaId, string MessageType, string PayloadJson);

/// <summary>
/// Result of one Graph API send attempt. <see cref="IsTransientFailure"/> vs a permanent one is
/// the retry/dead-letter fork the outbox dispatcher acts on (spec §4.2: 429/5xx retry with
/// backoff; 131026/131047/131049 fail fast) — see <c>GraphErrorClassifier</c> in
/// WaGateway.Infrastructure for how the HTTP status/body maps to these three outcomes.
/// </summary>
public sealed record GraphSendResult(
    bool Success,
    string? Wamid,
    bool IsTransientFailure,
    string? ErrorCode,
    string? ErrorMessage);

/// <summary>
/// Thin client over Meta's WhatsApp Cloud API messages endpoint
/// (<c>POST /{version}/{phone-number-id}/messages</c>). Implemented in WaGateway.Infrastructure
/// as a typed HttpClient pointed at Meta in production, at a local stub server in dev/tests (see
/// tools/MetaGraphSendApiStub) — same pattern as wa-admin-svc's <c>MetaGraphTemplateClient</c>
/// (issue #16). The access token is read from config and is NEVER logged.
/// </summary>
public interface IMetaGraphMessageClient
{
    Task<GraphSendResult> SendAsync(GraphSendRequest request, CancellationToken cancellationToken);
}
