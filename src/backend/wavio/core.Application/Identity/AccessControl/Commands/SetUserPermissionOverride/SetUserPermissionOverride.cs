using core.Application.Common.Interfaces;
using core.Application.Identity.Common;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.SharedDataModel.Entities.IdentityAccess;
using wavio.SharedDataModel.Enums;
using wavio.Utilities.Auth.Audit;
using wavio.Utilities.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.AccessControl.Commands.SetUserPermissionOverride;

/// <summary>Set or clear a per-user permission override. Effect "allow"/"deny" upserts it;
/// null/empty removes it (reverting to role-derived behaviour).
///
/// Optional (docs/rbac.md §7): <paramref name="ScopeType"/>/<paramref name="ScopeId"/> confine the
/// override to one node's subtree (null = global); <paramref name="ExpiresAt"/> time-boxes it
/// ("suspend until"); <paramref name="Reason"/> is recorded for the audit trail. The natural key
/// is (user, permission, scope) so a global and several scoped overrides can coexist.</summary>
public sealed record SetUserPermissionOverrideRequest(
    string PermissionCode,
    string? Effect,
    string? ScopeType = null,
    Guid? ScopeId = null,
    string? Reason = null,
    DateTimeOffset? ExpiresAt = null);

public sealed record SetUserPermissionOverrideCommand(Guid UserId, SetUserPermissionOverrideRequest Request, Guid? ActorId)
    : ICommand<bool>;

public class SetUserPermissionOverrideCommandHandler : ICommandHandler<SetUserPermissionOverrideCommand, bool>
{
    private readonly ICoreDbContext _db;
    private readonly IAuditWriter _audit;
    public SetUserPermissionOverrideCommandHandler(ICoreDbContext db, IAuditWriter audit)
    { _db = db; _audit = audit; }

    public async Task<bool> HandleAsync(SetUserPermissionOverrideCommand cmd, CancellationToken ct)
    {
        if (!await _db.Users.AnyAsync(u => u.Id == cmd.UserId && u.DeletedAt == null, ct)) return false;

        var effect = cmd.Request.Effect?.Trim().ToLowerInvariant();
        if (effect is not (null or "" or "allow" or "deny"))
            throw new ValidationException(new Dictionary<string, string[]> { ["effect"] = ["Must be 'allow', 'deny', or null."] });

        // Optional scope: null = global. If given, must be a known scope type; a non-platform
        // scope requires a node id, platform must not carry one.
        var scopeType = cmd.Request.ScopeType?.Trim().ToLowerInvariant();
        var scopeId = cmd.Request.ScopeId;
        if (scopeType is not null)
        {
            string[] valid = [ScopeType.Platform, ScopeType.Tenant];
            if (!valid.Contains(scopeType))
                throw new ValidationException(new Dictionary<string, string[]> { ["scopeType"] = [$"Must be one of: {string.Join(", ", valid)}."] });
            if (scopeType == ScopeType.Platform && scopeId is not null)
                throw new ValidationException(new Dictionary<string, string[]> { ["scopeId"] = ["Platform scope carries no id."] });
            if (scopeType != ScopeType.Platform && scopeId is null)
                throw new ValidationException(new Dictionary<string, string[]> { ["scopeId"] = ["A scope id is required for this scope type."] });
        }

        var perm = await _db.Permissions.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Code == cmd.Request.PermissionCode, ct)
            ?? throw new ValidationException(new Dictionary<string, string[]> { ["permissionCode"] = ["Unknown permission."] });

        // Match on the full natural key (user, permission, scope). Done in memory so null scope
        // columns compare correctly (SQL '= NULL' would never match a global override).
        var candidates = await _db.UserPermissionOverrides
            .Where(o => o.UserId == cmd.UserId && o.PermissionId == perm.Id)
            .ToListAsync(ct);
        var existing = candidates.FirstOrDefault(o => o.ScopeType == scopeType && o.ScopeId == scopeId);

        if (string.IsNullOrEmpty(effect))
        {
            if (existing is not null) _db.UserPermissionOverrides.Remove(existing);
        }
        else if (existing is null)
        {
            var now = DateTimeOffset.UtcNow;
            _db.UserPermissionOverrides.Add(new UserPermissionOverride
            {
                Id = Guid.NewGuid(),
                UserId = cmd.UserId, PermissionId = perm.Id, Effect = effect,
                ScopeType = scopeType, ScopeId = scopeId,
                Reason = cmd.Request.Reason, ExpiresAt = cmd.Request.ExpiresAt,
                GrantedAt = now, GrantedBy = cmd.ActorId, CreatedAt = now,
            });
        }
        else
        {
            existing.Effect = effect;
            existing.Reason = cmd.Request.Reason;
            existing.ExpiresAt = cmd.Request.ExpiresAt;
            existing.GrantedBy = cmd.ActorId;
            existing.GrantedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        // Live revocation: invalidate the user's existing tokens.
        await PermVersionBumper.BumpUserAsync(_db, cmd.UserId, ct);

        // Semantic audit: per-user permission override set/cleared (allow/deny at global or scoped level).
        await _audit.WriteAsync("permission.override", "user_permission_overrides", cmd.UserId,
            resourceDisplay: $"{cmd.Request.PermissionCode} = {(string.IsNullOrEmpty(effect) ? "cleared" : effect)}",
            newValues: new
            {
                cmd.UserId,
                cmd.Request.PermissionCode,
                Effect = string.IsNullOrEmpty(effect) ? null : effect,
                ScopeType = scopeType,
                ScopeId = scopeId,
                cmd.Request.ExpiresAt,
                cmd.Request.Reason
            },
            ct: ct);
        return true;
    }
}
