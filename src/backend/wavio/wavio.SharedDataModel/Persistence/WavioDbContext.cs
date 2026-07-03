using wavio.SharedDataModel.Entities.IdentityAccess;
using wavio.SharedDataModel.Entities.Kernel;
using wavio.SharedDataModel.Entities.TenancyOrg;
using Microsoft.EntityFrameworkCore;

namespace wavio.SharedDataModel.Persistence;

/// <summary>
/// Shared EF Core DbContext mapping the live PostgreSQL database (database-first).
/// Do NOT run migrations against this context — the DB schema is canonical.
///
/// Soft-delete query filters (HasQueryFilter(e => e.DeletedAt == null)):
///   tenancy: Tenant
///   identity_access: User, Role
///   kernel: FileAttachment
/// All other entities do not have deleted_at and have no global filter.
/// Use IgnoreQueryFilters() when you need to see soft-deleted rows.
/// </summary>
public class WavioDbContext : DbContext
{
    public WavioDbContext(DbContextOptions<WavioDbContext> options)
        : base(options) { }

    // tenancy
    public DbSet<Tenant> Tenants => Set<Tenant>();

    // identity_access
    public DbSet<User> Users => Set<User>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<UserScopeMembership> UserScopeMemberships => Set<UserScopeMembership>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserPermissionOverride> UserPermissionOverrides => Set<UserPermissionOverride>();
    public DbSet<OtpCode> OtpCodes => Set<OtpCode>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<LoginHistory> LoginHistories => Set<LoginHistory>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<PasswordReset> PasswordResets => Set<PasswordReset>();

    // kernel
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();
    public DbSet<OutboxConsumedEvent> OutboxConsumedEvents => Set<OutboxConsumedEvent>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();
    public DbSet<FileAttachment> FileAttachments => Set<FileAttachment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WavioDbContext).Assembly);
    }
}
