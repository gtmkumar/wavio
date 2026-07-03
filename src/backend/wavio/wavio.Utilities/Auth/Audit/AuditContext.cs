using wavio.SharedDataModel.Contracts;
using wavio.SharedDataModel.Entities.IdentityAccess;
using wavio.Utilities.Services;
using Microsoft.AspNetCore.Http;

namespace wavio.Utilities.Auth.Audit;

/// <summary>
/// Shared helpers used by BOTH the <see cref="AuditSaveChangesInterceptor"/> (entity-change audit)
/// and <see cref="IAuditWriter"/> (explicit command/denial audit) so every row is stamped identically.
/// </summary>
internal static class AuditContext
{
    /// <summary>Property-name fragments whose values must NEVER land in the long-retention audit
    /// table. Matched case-insensitively against the scalar property name. Covers credentials/secrets
    /// AND financial/identity/contact PII (e.g. UserProfile bank+PAN+UPI+KYC fields) so plaintext
    /// never gets snapshotted into old_values/new_values. This is the name-based backstop; the
    /// interceptor ALSO redacts any column bound to the PII crypto ValueConverter, so encrypted
    /// columns are masked even if their name misses these markers.</summary>
    private static readonly string[] SecretMarkers =
    [
        // credentials / secrets
        "password", "secret", "token", "hash", "salt", "privatekey", "apikey", "cvv", "otp",
        // financial / government-identity / contact PII
        "bank", "ifsc", "upi", "pan", "aadhaar", "aadhar", "license", "licence",
        "passport", "gstin", "accountnumber", "phone", "email", "dob",
    ];

    public static bool IsSecret(string propertyName)
        => SecretMarkers.Any(m => propertyName.Contains(m, StringComparison.OrdinalIgnoreCase));

    /// <summary>Redact a single value by property name (secrets/PII → "[redacted]").</summary>
    public static object? Redact(string propertyName, object? value)
        => value is not null && IsSecret(propertyName) ? "[redacted]" : value;

    /// <summary>
    /// Stamp actor / tenant / request context + timestamps on a row. tenant_id is taken from
    /// <see cref="ICurrentTenant"/> (NOT the mutated entity) so it equals the RLS session var
    /// (app.tenant_id) — the audit INSERT's WITH CHECK then passes by construction whenever
    /// the business write in the same transaction passed. occurred_at is UtcNow so the row always
    /// lands in the always-present current-month partition.
    /// </summary>
    public static void Fill(AuditLog log, ICurrentTenant tenant, ICurrentUser user, HttpContext? http)
    {
        var now = DateTimeOffset.UtcNow;
        log.OccurredAt = now;
        log.CreatedAt  = now;

        // Tenant — the RLS invariant above.
        log.TenantId = tenant.TenantId;

        if (user.UserId is { } uid)
        {
            log.ActorType = "user";
            log.ActorUserId = uid;
            log.CreatedBy   = uid;
            log.ActorDisplay ??= user.Email ?? user.Phone;
        }
        else
        {
            // No principal → a background worker / seed / job path.
            log.ActorType = http is null ? "system" : "api";
        }

        // Request context (best-effort; null on non-HTTP paths).
        if (http is not null)
        {
            log.IpAddress ??= http.Connection?.RemoteIpAddress;
            var ua = http.Request?.Headers.UserAgent.ToString();
            if (!string.IsNullOrEmpty(ua)) log.UserAgent ??= ua.Length > 1000 ? ua[..1000] : ua;
            if (Guid.TryParse(http.TraceIdentifier, out var reqId)) log.RequestId ??= reqId;
        }
        if (System.Diagnostics.Activity.Current is { } act
            && Guid.TryParse(act.TraceId.ToString(), out var corr))
            log.CorrelationId ??= corr;
    }
}
