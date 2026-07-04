namespace WaGateway.Application.Common.Interfaces;

/// <summary>Window state as needed for the send-policy decision (ADR-005) — a deliberately
/// narrow local shape, not a shared DTO with wa-intel-svc (services don't share Application-layer
/// types across a process boundary).</summary>
public sealed record WindowStateResult(bool CsOpen, bool CtwaOpen);

/// <summary>
/// Consults the Session Window Manager (wa-intel-svc) before a send is allowed (spec §4.2,
/// ADR-005). Implemented in WaGateway.Infrastructure as an HTTP call to wa-intel-svc's
/// <c>GET /v1/windows/{waId}</c> with a short-TTL local cache — a service-to-service hop rather
/// than a direct read of the <c>sessions</c> schema, because that schema is owned by wa-intel-svc
/// (DDD bounded-context ownership: only the owning service reads/writes its own tables) and this
/// keeps the p95 &lt;2s send budget intact via the cache rather than by reaching across the
/// ownership boundary. See the issue #14 decisions memory for the full justification and the
/// alternative considered (a direct, cross-service DB read) and why it was rejected.
/// </summary>
public interface IWindowStateClient
{
    /// <summary>Null if the window-state service could not be reached at all (fail-closed at the
    /// caller: treat as "no open window" for a free-form send, per ADR-005 — cost transparency
    /// over convenience means we never guess a window open when we can't confirm it).</summary>
    Task<WindowStateResult?> GetWindowStateAsync(Guid phoneNumberId, string waId, CancellationToken cancellationToken);
}
