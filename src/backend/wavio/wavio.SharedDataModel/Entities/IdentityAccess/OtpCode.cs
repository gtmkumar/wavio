namespace wavio.SharedDataModel.Entities.IdentityAccess;

/// <summary>OTP code record (identity_access.otp_codes).
/// Has created_at only — no updated_at, no version, no deleted_at.</summary>
public class OtpCode
{
    public Guid Id { get; set; }
    public string Purpose { get; set; } = null!;
    public string Identifier { get; set; } = null!;
    public string IdentifierType { get; set; } = null!;
    public string CodeHash { get; set; } = null!;

    /// <summary>
    /// Per-row random salt (hex-encoded 16 bytes) used for HMAC-SHA256 code hashing.
    /// NULL on rows written before the salted-hash migration — those rows use the legacy
    /// unsalted SHA-256 path and expire within TTL (≤ 5 minutes).
    /// </summary>
    public string? CodeSalt { get; set; }

    public Guid? UserId { get; set; }

    public Guid? ReferenceId { get; set; }
    public string? ReferenceType { get; set; }
    public short Attempts { get; set; }
    public short MaxAttempts { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public IPAddress? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // Navigations
    public User? User { get; set; }
}
