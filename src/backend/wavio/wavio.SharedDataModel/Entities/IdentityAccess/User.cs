using wavio.SharedDataModel.Common;

namespace wavio.SharedDataModel.Entities.IdentityAccess;

/// <summary>Staff/admin user identity (identity_access.users).</summary>
public class User : IAuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; }
    public string? PhoneE164 { get; set; }
    public string? Email { get; set; }
    public string? PasswordHash { get; set; }
    public DateTimeOffset? PasswordChangedAt { get; set; }
    public bool MustChangePassword { get; set; }
    public bool MfaEnabled { get; set; }
    public string? MfaSecret { get; set; }
    public string[]? MfaBackupCodes { get; set; }
    public string UserType { get; set; } = null!;

    public string Locale { get; set; } = null!;
    public string Timezone { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTimeOffset? LastLoginAt { get; set; }
    public IPAddress? LastLoginIp { get; set; }
    public DateTimeOffset? LastActiveAt { get; set; }
    public short FailedAttempts { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset? EmailVerifiedAt { get; set; }
    public DateTimeOffset? PhoneVerifiedAt { get; set; }
    public string? InvitationToken { get; set; }
    public DateTimeOffset? InvitationSentAt { get; set; }
    public DateTimeOffset? InvitationAcceptedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public int Version { get; set; }
    /// <summary>Bumped whenever the user's effective permissions change (role/entitlement).
    /// Stamped into the JWT; a token with a stale value is rejected to force a refresh.</summary>
    public int PermVersion { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    // Navigations
    public UserProfile? Profile { get; set; }
    public ICollection<UserScopeMembership> ScopeMemberships { get; set; } = [];
    public ICollection<OtpCode> OtpCodes { get; set; } = [];
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
    public ICollection<LoginHistory> LoginHistories { get; set; } = [];
    public ICollection<PasswordReset> PasswordResets { get; set; } = [];
}
