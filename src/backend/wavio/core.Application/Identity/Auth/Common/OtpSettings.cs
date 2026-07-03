namespace core.Application.Identity.Auth.Common;

public sealed class OtpSettings
{
    public const string SectionName = "Otp";

    public int TtlMinutes  { get; set; } = 5;
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Minimum seconds that must elapse before a new OTP can be issued for the same identifier+purpose.</summary>
    public int ResendCooldownSeconds { get; set; } = 60;

    // ── Rolling-window lockout ────────────────────────────────────────────────

    /// <summary>
    /// Length of the rolling window (minutes) over which failed verify attempts are counted.
    /// If the sum of Attempts across all otp_codes rows for an identifier within this window
    /// reaches <see cref="LockoutThreshold"/>, both send and verify are blocked for
    /// <see cref="LockoutDurationMinutes"/>.
    /// </summary>
    public int LockoutWindowMinutes { get; set; } = 15;

    /// <summary>
    /// Number of failed verify attempts (across any/all OTP rows for the same identifier)
    /// within <see cref="LockoutWindowMinutes"/> that triggers a temporary lockout.
    /// </summary>
    public int LockoutThreshold { get; set; } = 10;

    /// <summary>Duration (minutes) of the temporary lockout once the threshold is exceeded.</summary>
    public int LockoutDurationMinutes { get; set; } = 15;

    // ── Salted HMAC-SHA256 hashing ────────────────────────────────────────────

    /// <summary>
    /// Base64-encoded 32-byte HMAC-SHA256 key used to hash OTP codes.
    /// In Development a static well-known dev key is used if this is null/empty.
    /// In Production MUST be injected via environment variable <c>Otp__HmacKey</c>
    /// (or a secrets manager); the service throws at startup if absent outside Dev.
    /// </summary>
    public string? HmacKey { get; set; }

    // ── Test/staging master code ──────────────────────────────────────────────

    /// <summary>
    /// TESTING ONLY: when set (e.g. "123456"), this code is accepted as a valid
    /// OTP for ANY login verify (rider, staff, customer, OAuth consent) IN
    /// NON-PRODUCTION ENVIRONMENTS, so testers and app-store reviewers can sign
    /// in without SMS/WhatsApp delivery. The normal send flow still runs first —
    /// an unexpired OTP row must exist for the identifier.
    ///
    /// Hard-blocked in Production twice over: the verify check ignores it when
    /// IHostEnvironment.IsProduction(), and Identity REFUSES TO START in
    /// Production with this set. Leave null/empty everywhere except dev/staging.
    /// </summary>
    public string? TestCode { get; set; }
}
