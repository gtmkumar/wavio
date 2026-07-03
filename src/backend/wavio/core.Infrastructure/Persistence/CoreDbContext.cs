using core.Application.Common.Interfaces;
using wavio.SharedDataModel.Entities.IdentityAccess;
using wavio.SharedDataModel.Entities.Kernel;
using wavio.SharedDataModel.Entities.TenancyOrg;
using wavio.SharedDataModel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace core.Infrastructure.Persistence;

/// <summary>
/// Adapts the shared <see cref="WavioDbContext"/> to <see cref="ICoreDbContext"/>, exposing
/// only the entity sets the core slices use. Lets Application handlers depend on the context
/// surface they own without taking a dependency on the shared concrete context.
/// </summary>
public sealed class CoreDbContext : ICoreDbContext
{
    private readonly WavioDbContext _db;

    public CoreDbContext(WavioDbContext db) => _db = db;

    public DbSet<Tenant> Tenants => _db.Tenants;

    public DbSet<Role> Roles => _db.Roles;
    public DbSet<User> Users => _db.Users;
    public DbSet<UserProfile> UserProfiles => _db.UserProfiles;
    public DbSet<UserScopeMembership> UserScopeMemberships => _db.UserScopeMemberships;
    public DbSet<Permission> Permissions => _db.Permissions;
    public DbSet<RolePermission> RolePermissions => _db.RolePermissions;
    public DbSet<UserPermissionOverride> UserPermissionOverrides => _db.UserPermissionOverrides;

    public DbSet<RefreshToken> RefreshTokens => _db.RefreshTokens;
    public DbSet<LoginHistory> LoginHistories => _db.LoginHistories;
    public DbSet<OtpCode> OtpCodes => _db.OtpCodes;
    public DbSet<PasswordReset> PasswordResets => _db.PasswordResets;

    public DbSet<SystemSetting> SystemSettings => _db.SystemSettings;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        _db.SaveChangesAsync(cancellationToken);
}
