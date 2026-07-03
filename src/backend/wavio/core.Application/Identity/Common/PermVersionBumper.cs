using core.Application.Common.Interfaces;
using wavio.SharedDataModel.Enums;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.Common;

/// <summary>
/// Bumps <c>users.perm_version</c> so already-issued tokens are treated as stale and the
/// holder is forced to refresh (picking up the new permissions/entitlement). Atomic bulk
/// UPDATEs via ExecuteUpdate — run AFTER the handler's own SaveChanges. Best-effort: a bump
/// failure must never fail the underlying grant, so callers wrap in try/catch where needed.
/// See docs/rbac-entitlement-plan.md / the gap doc's live-revocation item.
/// </summary>
public static class PermVersionBumper
{
    public static Task BumpUserAsync(ICoreDbContext db, Guid userId, CancellationToken ct) =>
        db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.PermVersion, u => u.PermVersion + 1), ct);

    public static Task BumpUsersAsync(ICoreDbContext db, IReadOnlyCollection<Guid> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0) return Task.CompletedTask;
        return db.Users.Where(u => userIds.Contains(u.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.PermVersion, u => u.PermVersion + 1), ct);
    }

    /// <summary>Bump every user holding a role (active memberships) — e.g. after a role-cell edit.</summary>
    public static async Task BumpRoleHoldersAsync(ICoreDbContext db, Guid roleId, CancellationToken ct)
    {
        var ids = await db.UserScopeMemberships.AsNoTracking()
            .Where(m => m.RoleId == roleId && m.RevokedAt == null)
            .Select(m => m.UserId).Distinct().ToListAsync(ct);
        await BumpUsersAsync(db, ids, ct);
    }
}
