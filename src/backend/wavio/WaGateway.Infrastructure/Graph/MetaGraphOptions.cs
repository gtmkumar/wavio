namespace WaGateway.Infrastructure.Graph;

/// <summary>
/// Configuration for <see cref="MetaGraphMessageClient"/> (issue #14). Bound from
/// <c>Meta:Graph</c>. <see cref="AccessToken"/> is a single system-user token read from config —
/// real per-WABA, envelope-encrypted token storage arrives with onboarding (issue #6); this is
/// enough to exercise the full send flow against a stub server in dev/tests. Same shape as
/// wa-admin-svc's <c>MetaGraphOptions</c> (issue #16) — reimplemented locally, not shared,
/// per bounded-context isolation.
/// </summary>
public sealed class MetaGraphOptions
{
    public const string SectionName = "Meta:Graph";

    /// <summary>e.g. https://graph.facebook.com in production, or the local stub server's URL.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>e.g. "v21.0".</summary>
    public string ApiVersion { get; set; } = "v21.0";

    /// <summary>System-user bearer token. Never logged.</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Meta's per-number throughput field (spec §4.2: default 80 MPS, auto-upgradable to
    /// 1,000 MPS). Config-only for Wave 1 — the real per-number value arrives with onboarding
    /// (issue #6); until then every number uses this one configured default.
    /// </summary>
    public int DefaultThroughputPerSecond { get; set; } = 80;

    /// <summary>
    /// Messaging-tier headroom (unique marketing-initiated recipients per rolling 24h) — spec
    /// §4.2's 250 → 1K → 10K → 100K → unlimited ladder. Config-only for Wave 1 (real tier arrives
    /// with onboarding); 0 or negative means unlimited.
    /// </summary>
    public int DefaultMessagingTierPerDay { get; set; } = 250;

    /// <summary>
    /// Explicit HttpClient timeout for Graph requests (security review, PR #45, S1). MUST be
    /// strictly less than <c>Outbox:StaleLockSeconds</c> — enforced by a startup sanity check in
    /// DependencyInjection.cs — so a slow Graph response fails fast (classified transient,
    /// retried through the normal backoff path) rather than outliving the stale-lock reclaim
    /// window: without this, a call slower than the reclaim window gets its lease stolen WHILE
    /// STILL IN FLIGHT, the reclaimer re-sends, and if both eventually succeed the customer gets
    /// a duplicate message. 0 (default) means "not explicitly configured" —
    /// DependencyInjection.cs computes a safe default with headroom below
    /// <c>Outbox:StaleLockSeconds</c> instead of using this raw value.
    /// </summary>
    public int TimeoutSeconds { get; set; }
}
