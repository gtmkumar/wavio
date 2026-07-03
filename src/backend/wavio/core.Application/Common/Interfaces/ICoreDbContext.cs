using wavio.SharedDataModel.Entities.IdentityAccess;
using wavio.SharedDataModel.Entities.Kernel;
using wavio.SharedDataModel.Entities.TenancyOrg;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Common.Interfaces;

/// <summary>
/// The core context's data-access surface, exposed to Application handlers as an interface
/// (no repositories). Backed by the shared <c>WavioDbContext</c> via an adapter in
/// core.Infrastructure. Handlers inject this and write EF Core LINQ directly.
/// Only the entity sets the core slices touch are surfaced here.
/// </summary>
public interface ICoreDbContext
{
    // ─── Tenancy ───────────────────────────────────────────────────────────
    DbSet<Tenant> Tenants { get; }

    // ─── Identity access (admin user/access-control) ──────────────────────
    DbSet<Role> Roles { get; }
    DbSet<User> Users { get; }
    DbSet<UserProfile> UserProfiles { get; }
    DbSet<UserScopeMembership> UserScopeMemberships { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<UserPermissionOverride> UserPermissionOverrides { get; }

    // ─── Identity access (system auth: login / OTP / refresh / password reset) ─
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<LoginHistory> LoginHistories { get; }
    DbSet<OtpCode> OtpCodes { get; }
    DbSet<PasswordReset> PasswordResets { get; }

    // ─── Kernel (system settings store) ───────────────────────────────────
    DbSet<SystemSetting> SystemSettings { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
