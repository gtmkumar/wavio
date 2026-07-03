using core.Application.Common.Interfaces;
using wavio.SharedDataModel.Entities.IdentityAccess;
using wavio.SharedDataModel.Entities.TenancyOrg;
using wavio.SharedDataModel.Enums;
using wavio.SharedDataModel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace core.Infrastructure.Seeders;

/// <summary>
/// Idempotent bootstrap seeder.
/// Only auto-runs in Development. If --seed is passed in Production, throws to prevent
/// accidental credential seeding in live environments.
/// Admin password is read from Seeder:AdminPassword config (defaults to "Admin@123" in Development).
///
/// Seeds:
/// 1. Permissions catalog (module.action codes) — matches the endpoints this template ships with.
///    Add a row per permission code as you add new "permission:&lt;code&gt;" endpoints.
/// 2. System roles (platform_admin, tenant_admin, staff) with scope types
/// 3. role_permissions: platform_admin → all; tenant_admin → tenant-scoped subset; staff → read-only
/// 4. One tenant row (if none exists)
/// 5. One platform_admin user (admin@wavio.local) with a platform-scope membership
///
/// Seeding writes bootstrap rows BEFORE any HTTP request has established a tenant context.
/// It must therefore run on a privileged RLS-bypassing (admin/postgres) <see cref="WavioDbContext"/>
/// — see <see cref="wavio.SharedDataModel.SeedingSupport.CreatePrivilegedContext"/>.
/// </summary>
public sealed class IdentitySeeder
{
    private readonly WavioDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly ILogger<IdentitySeeder> _logger;

    public IdentitySeeder(
        WavioDbContext db,
        IPasswordHasher hasher,
        IHostEnvironment env,
        IConfiguration config,
        ILogger<IdentitySeeder> logger)
    {
        _db = db;
        _hasher = hasher;
        _env = env;
        _config = config;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        // Hard guard — seeder must never run unguarded in Production
        if (!_env.IsDevelopment())
        {
            throw new InvalidOperationException(
                "The identity seeder may only run in Development. " +
                "Use a dedicated migration / bootstrap tool for production environments.");
        }

        _logger.LogInformation("Running identity seeders...");

        var permissions = await SeedPermissionsAsync(ct);
        var roles = await SeedRolesAsync(ct);
        await SeedRolePermissionsAsync(permissions, roles, ct);
        var tenant = await SeedTenantAsync(ct);
        await SeedAdminUserAsync(tenant, roles, ct);

        _logger.LogInformation("Seeding complete.");
    }

    // ─── 1. Permissions ────────────────────────────────────────────────────

    private static readonly (string Code, string Module, string Action, string Name, string Risk)[] PermissionDefs =
    [
        ("users.list",           "users",       "list",       "List users",              RiskLevel.Low),
        ("users.read",           "users",       "read",       "View user",               RiskLevel.Low),
        ("users.create",         "users",       "create",     "Create user",             RiskLevel.Normal),
        ("users.update",         "users",       "update",     "Update user",             RiskLevel.Normal),
        ("users.deactivate",     "users",       "deactivate", "Activate/suspend user",   RiskLevel.High),
        ("users.set_password",   "users",       "set_password","Set user password",      RiskLevel.High),
        ("users.set_type",       "users",       "set_type",   "Change user type",        RiskLevel.Critical),
        ("users.read_financial", "users",       "read",       "View financial PII",      RiskLevel.High),
        ("roles.list",           "roles",       "list",       "List roles",              RiskLevel.Low),
        ("roles.manage",         "roles",       "manage",     "Create/edit/delete roles",RiskLevel.Critical),
        ("permissions.list",     "permissions", "list",       "List permissions",        RiskLevel.Low),
        ("permissions.assign",   "permissions", "assign",     "Assign role permissions", RiskLevel.Critical),
        ("memberships.grant",    "memberships", "grant",      "Grant a membership",      RiskLevel.Critical),
        ("memberships.revoke",   "memberships", "revoke",     "Revoke a membership",     RiskLevel.High),
        ("widgets.manage",       "widgets",     "manage",     "Manage widgets (example)",RiskLevel.Normal),
        ("messages.send",        "messages",    "send",       "Send an outbound WhatsApp message", RiskLevel.Normal),
    ];

    private async Task<Dictionary<string, Permission>> SeedPermissionsAsync(CancellationToken ct)
    {
        var existing = await _db.Permissions.ToDictionaryAsync(p => p.Code, ct);
        var now = DateTimeOffset.UtcNow;

        foreach (var def in PermissionDefs)
        {
            if (existing.ContainsKey(def.Code)) continue;

            var perm = new Permission
            {
                Id = Guid.NewGuid(),
                Code = def.Code,
                Module = def.Module,
                Action = def.Action,
                Name = def.Name,
                IsSystem = true,
                RequiresScope = true,
                RiskLevel = def.Risk,
                Status = "active",
                CreatedAt = now,
                UpdatedAt = now,
            };
            _db.Permissions.Add(perm);
            existing[def.Code] = perm;
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded {Count} permission(s).", PermissionDefs.Length);
        return existing;
    }

    // ─── 2. Roles ──────────────────────────────────────────────────────────

    private static readonly (string Code, string Name, string ScopeType, short Priority)[] RoleDefs =
    [
        ("platform_admin", "Platform Admin", ScopeType.Platform, 10),
        ("tenant_admin",   "Tenant Admin",   ScopeType.Tenant,   20),
        ("staff",          "Staff",          ScopeType.Tenant,   60),
    ];

    private async Task<Dictionary<string, Role>> SeedRolesAsync(CancellationToken ct)
    {
        var existing = await _db.Roles.IgnoreQueryFilters()
            .Where(r => r.TenantId == null)
            .ToDictionaryAsync(r => r.Code, ct);
        var now = DateTimeOffset.UtcNow;

        foreach (var def in RoleDefs)
        {
            if (existing.ContainsKey(def.Code)) continue;

            var role = new Role
            {
                Id = Guid.NewGuid(),
                TenantId = null, // system role — visible to every tenant
                Code = def.Code,
                Name = def.Name,
                ScopeType = def.ScopeType,
                IsSystem = true,
                IsAssignable = true,
                Priority = def.Priority,
                Status = "active",
                CreatedAt = now,
                UpdatedAt = now,
            };
            _db.Roles.Add(role);
            existing[def.Code] = role;
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded {Count} role(s).", RoleDefs.Length);
        return existing;
    }

    // ─── 3. Role → Permission grants ─────────────────────────────────────────

    private async Task SeedRolePermissionsAsync(
        Dictionary<string, Permission> permissions, Dictionary<string, Role> roles, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var existing = await _db.RolePermissions.Select(rp => new { rp.RoleId, rp.PermissionId }).ToListAsync(ct);
        var existingSet = existing.Select(x => (x.RoleId, x.PermissionId)).ToHashSet();

        void Grant(string roleCode, params string[] permissionCodes)
        {
            if (!roles.TryGetValue(roleCode, out var role)) return;
            foreach (var code in permissionCodes)
            {
                if (!permissions.TryGetValue(code, out var perm)) continue;
                if (existingSet.Contains((role.Id, perm.Id))) continue;

                _db.RolePermissions.Add(new RolePermission
                {
                    Id = Guid.NewGuid(),
                    RoleId = role.Id,
                    PermissionId = perm.Id,
                    Effect = "allow",
                    GrantedAt = now,
                    CreatedAt = now,
                });
                existingSet.Add((role.Id, perm.Id));
            }
        }

        // platform_admin: every permission.
        Grant("platform_admin", PermissionDefs.Select(p => p.Code).ToArray());

        // tenant_admin: full control within a tenant except the highest-risk actions.
        Grant("tenant_admin",
            "users.list", "users.read", "users.create", "users.update", "users.deactivate",
            "users.set_password", "roles.list", "roles.manage", "permissions.list",
            "permissions.assign", "memberships.grant", "memberships.revoke", "widgets.manage",
            "messages.send");

        // staff: read-only + the example feature + day-to-day messaging.
        Grant("staff", "users.list", "users.read", "roles.list", "permissions.list", "widgets.manage",
            "messages.send");

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded role→permission grants.");
    }

    // ─── 4. Tenant ─────────────────────────────────────────────────────────

    private async Task<Tenant> SeedTenantAsync(CancellationToken ct)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters().OrderBy(t => t.CreatedAt).FirstOrDefaultAsync(ct);
        if (tenant is not null) return tenant;

        var now = DateTimeOffset.UtcNow;
        tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Code = "default",
            Name = "Default Tenant",
            CurrencyCode = "USD",
            CountryCode = "US",
            Timezone = "UTC",
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded default tenant {TenantId}.", tenant.Id);
        return tenant;
    }

    // ─── 5. Admin user ───────────────────────────────────────────────────────

    private async Task SeedAdminUserAsync(Tenant tenant, Dictionary<string, Role> roles, CancellationToken ct)
    {
        const string AdminEmail = "admin@wavio.local";

        // Admin password read from config; fallback to "Admin@123" only in Development.
        var adminPassword = _config["Seeder:AdminPassword"] ?? "Admin@123";

        var now = DateTimeOffset.UtcNow;

        var user = await _db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == AdminEmail, ct);

        if (user is null)
        {
            user = new User
            {
                Id = Guid.NewGuid(),
                Email = AdminEmail,
                PasswordHash = _hasher.Hash(adminPassword),
                UserType = UserType.PlatformAdmin,
                Status = UserStatus.Active,
                EmailVerifiedAt = now,
                Locale = "en-US",
                Timezone = "UTC",
                FailedAttempts = 0,
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync(ct);

            _db.UserProfiles.Add(new UserProfile
            {
                UserId = user.Id,
                FirstName = "Platform",
                LastName = "Admin",
                DisplayName = "Platform Admin",
                Preferences = "{}",
                Metadata = "{}",
                Status = "active",
                CreatedAt = now,
                UpdatedAt = now
            });
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Seeded admin user {UserId} ({Email}).", user.Id, AdminEmail);
        }

        // Ensure platform-scope membership to platform_admin role
        if (roles.TryGetValue("platform_admin", out var platformAdminRole))
        {
            var hasMembership = await _db.UserScopeMemberships
                .AnyAsync(m => m.UserId == user.Id
                            && m.ScopeType == ScopeType.Platform
                            && m.RoleId == platformAdminRole.Id
                            && m.RevokedAt == null, ct);

            if (!hasMembership)
            {
                _db.UserScopeMemberships.Add(new UserScopeMembership
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    ScopeType = ScopeType.Platform,
                    ScopeId = null,
                    RoleId = platformAdminRole.Id,
                    IsPrimary = true,
                    GrantedAt = now,
                    Metadata = "{}",
                    CreatedAt = now
                });
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation("Granted platform_admin membership to admin user.");
            }
        }
    }
}
