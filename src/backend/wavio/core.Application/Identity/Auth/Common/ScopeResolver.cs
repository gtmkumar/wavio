using core.Application.Common.Interfaces;
using wavio.SharedDataModel.Entities.IdentityAccess;
using wavio.SharedDataModel.Enums;
using wavio.Utilities.Auth;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.Auth.Common;

/// <summary>
/// Resolves a user's active scope memberships → roles → permissions,
/// and builds TokenClaims ready for JWT issuance.
/// </summary>
public static class ScopeResolver
{
    /// <summary>
    /// Loads the user's active memberships + role permissions from the DB,
    /// then picks an active scope (primary, or override if valid) and builds claims.
    /// </summary>
    public static async Task<TokenClaims> BuildTokenClaimsAsync(
        ICoreDbContext db,
        User user,
        string? requestedScopeType = null,
        Guid? requestedScopeId = null,
        CancellationToken ct = default)
    {
        // Load active memberships with roles and their permissions
        var memberships = await db.UserScopeMemberships
            .AsNoTracking()
            .Where(m => m.UserId == user.Id
                     && m.RevokedAt == null
                     && (m.ExpiresAt == null || m.ExpiresAt > DateTimeOffset.UtcNow))
            .Include(m => m.Role)
                .ThenInclude(r => r.RolePermissions)
                    .ThenInclude(rp => rp.Permission)
            .ToListAsync(ct);

        // Select active scope: requested → primary → first
        UserScopeMembership? activeMembership = null;

        if (requestedScopeType is not null)
        {
            activeMembership = memberships.FirstOrDefault(m =>
                m.ScopeType == requestedScopeType
                && m.ScopeId == requestedScopeId);
        }

        activeMembership ??= memberships.FirstOrDefault(m => m.IsPrimary)
                          ?? memberships.FirstOrDefault();

        // Two-level tenancy: "platform" (global) or "tenant" (scoped to one Tenant row).
        Guid? tenantId = activeMembership?.ScopeType == ScopeType.Tenant
            ? activeMembership.ScopeId
            : null;

        // The ancestor-or-self key set for the active node. "platform" is an ancestor of
        // every node; a tenant membership covers everything beneath it.
        static string NodeKey(string type, Guid? id) => id is { } g ? $"{type}:{g}" : type;
        var ancestorKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ScopeType.Platform };
        if (activeMembership is not null) ancestorKeys.Add(NodeKey(activeMembership.ScopeType, activeMembership.ScopeId));

        // Resolve effective permissions across every membership whose scope node is ANCESTOR-OR-SELF
        // of the active node — a platform-level role covers every tenant beneath it. Then layer
        // per-user overrides. Allow/deny semantics, DENY WINS:
        // effective = (role-allowed − role-denied ∪ user-allow) − user-deny.
        var roleAllowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roleDenied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in memberships.Where(m => ancestorKeys.Contains(NodeKey(m.ScopeType, m.ScopeId))))
            foreach (var rp in m.Role.RolePermissions)
            {
                if (rp.Effect == "deny") roleDenied.Add(rp.Permission.Code);
                else roleAllowed.Add(rp.Permission.Code);
            }

        // Per-user overrides: skip EXPIRED rows; a scoped override applies only when its node is
        // ancestor-or-self of the active scope; a global (null-scope) override applies everywhere.
        // Deny always wins.
        var nowUtc = DateTimeOffset.UtcNow;
        var overrides = await db.UserPermissionOverrides.AsNoTracking()
            .Where(o => o.UserId == user.Id && (o.ExpiresAt == null || o.ExpiresAt > nowUtc))
            .Select(o => new { o.Effect, o.Permission.Code, o.ScopeType, o.ScopeId })
            .ToListAsync(ct);
        var userAllow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var userDeny = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in overrides)
        {
            // Deny always wins: deny overrides apply across scopes; only ALLOW overrides are
            // subtree-limited to the active node. A descendant-scoped DENY (e.g. a tenant
            // suspension) must still bite when the user operates at the platform level —
            // otherwise the suspension would silently vanish at the broader active scope (fail-open).
            if (o.Effect != "deny" && o.ScopeType is not null && !ancestorKeys.Contains(NodeKey(o.ScopeType, o.ScopeId)))
                continue; // scoped ALLOW outside the active subtree → does not apply
            (o.Effect == "deny" ? userDeny : userAllow).Add(o.Code);
        }

        // effective = (roleAllowed − roleDenied ∪ userAllow) − userDeny
        var effective = new HashSet<string>(roleAllowed, StringComparer.OrdinalIgnoreCase);
        effective.ExceptWith(roleDenied);
        effective.UnionWith(userAllow);
        effective.ExceptWith(userDeny);

        var permissions = new HashSet<string>(effective, StringComparer.OrdinalIgnoreCase);

        // Every node the user holds an active membership at → the scope_nodes claim, so mutating
        // handlers can enforce the ancestor-or-self boundary per request via ICurrentUser.IsWithinScope.
        var scopeNodes = string.Join(' ', memberships
            .Select(m => NodeKey(m.ScopeType, m.ScopeId))
            .Distinct(StringComparer.OrdinalIgnoreCase));

        // Step-up: the subset of the caller's effective permissions that are high/critical and
        // therefore require a fresh OTP re-verification. Baked into the token so the DB-less per-host
        // authorization handlers can decide "is this action risky" with a claim read (no risk catalog
        // per host). platform_admin bypasses the membership check but NOT step-up, so it must carry the
        // FULL high/critical catalog. Runs only at token mint (login/refresh/step-up) — not a hot path.
        List<string> stepUpPerms = user.UserType == UserType.PlatformAdmin
            ? await db.Permissions.AsNoTracking()
                .Where(p => p.RiskLevel == RiskLevel.High || p.RiskLevel == RiskLevel.Critical)
                .Select(p => p.Code)
                .ToListAsync(ct)
            : await db.Permissions.AsNoTracking()
                .Where(p => permissions.Contains(p.Code) && (p.RiskLevel == RiskLevel.High || p.RiskLevel == RiskLevel.Critical))
                .Select(p => p.Code)
                .ToListAsync(ct);

        return new TokenClaims(
            UserId:      user.Id,
            UserType:    user.UserType,
            Email:       user.Email,
            Phone:       user.PhoneE164,
            ScopeType:   activeMembership?.ScopeType,
            ScopeId:     activeMembership?.ScopeId,
            TenantId:    tenantId,
            Permissions: string.Join(' ', permissions),
            PermVersion: user.PermVersion,
            ScopeNodes:  scopeNodes,
            StepUpPerms: string.Join(' ', stepUpPerms));
    }
}
