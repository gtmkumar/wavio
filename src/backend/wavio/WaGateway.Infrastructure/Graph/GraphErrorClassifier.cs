namespace WaGateway.Infrastructure.Graph;

/// <summary>
/// Classifies a failed Graph send as transient (retry with backoff) or permanent (fail fast),
/// per spec §4.2: 429/5xx → exponential backoff, max 5 attempts, jitter; Meta error codes 131026
/// (not on WhatsApp), 131047 (re-engagement required), 131049 (per-user marketing limit) → fail
/// fast regardless of the HTTP status Meta wrapped them in. Pure — no I/O — so every combination
/// is unit testable without a real Graph API or stub server.
/// </summary>
public static class GraphErrorClassifier
{
    /// <summary>Meta error codes that are NEVER worth retrying — the condition they describe
    /// cannot change by trying again (spec §4.2).</summary>
    public static readonly IReadOnlyCollection<int> PermanentMetaErrorCodes = [131026, 131047, 131049];

    /// <summary>
    /// True when this failed send should be retried with backoff rather than dead-lettered
    /// immediately. A recognized permanent Meta error code always wins over the HTTP status —
    /// Meta sometimes wraps 131047/131049 in a 400, which would otherwise misclassify as
    /// permanent-by-default-4xx (already correct) but a permanent code wrapped in a 5xx (Meta
    /// infra having a bad day while still telling you the send would never have succeeded
    /// anyway) must not be retried either.
    /// </summary>
    public static bool IsTransient(int httpStatusCode, int? metaErrorCode)
    {
        if (metaErrorCode.HasValue && PermanentMetaErrorCodes.Contains(metaErrorCode.Value))
        {
            return false;
        }

        return httpStatusCode == 429 || httpStatusCode >= 500;
    }
}
