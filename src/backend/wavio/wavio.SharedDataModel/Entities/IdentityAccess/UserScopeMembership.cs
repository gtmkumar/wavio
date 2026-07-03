namespace wavio.SharedDataModel.Entities.IdentityAccess;

/// <summary>Assigns a user to a role within a specific scope (identity_access.user_scope_memberships).
/// Has created_at, created_by only — no updated_at, no version, no deleted_at.</summary>
public class UserScopeMembership
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string ScopeType { get; set; } = null!;
    public Guid? ScopeId { get; set; }
    public Guid RoleId { get; set; }
    public bool IsPrimary { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
    public Guid? GrantedBy { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? RevokedBy { get; set; }
    public string? RevokedReason { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string Metadata { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    // Navigations
    public User User { get; set; } = null!;
    public Role Role { get; set; } = null!;
}
