namespace wavio.SharedDataModel.Entities.IdentityAccess;

/// <summary>JWT refresh token with rotation support (identity_access.refresh_tokens).
/// Has created_at only — no updated_at, no version, no deleted_at.</summary>
public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }

    public string TokenHash { get; set; } = null!;
    public Guid FamilyId { get; set; }
    public Guid? ParentTokenId { get; set; }
    public string? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public string? DeviceOs { get; set; }
    public IPAddress? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // Navigations — self-referential for family/parent within same table
    public User? User { get; set; }
    public RefreshToken? Family { get; set; }
    public RefreshToken? ParentToken { get; set; }
}
