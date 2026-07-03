namespace wavio.SharedDataModel.Entities.IdentityAccess;

/// <summary>Password-reset token record (identity_access.password_resets).
/// Has created_at, updated_at, created_by, updated_by, status — no version, no deleted_at.</summary>
public class PasswordReset
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }

    public string TokenHash { get; set; } = null!;
    public IPAddress? RequestedIp { get; set; }
    public string? RequestedUserAgent { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public IPAddress? UsedIp { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public string Status { get; set; } = null!;

    // Navigations
    public User? User { get; set; }
}
