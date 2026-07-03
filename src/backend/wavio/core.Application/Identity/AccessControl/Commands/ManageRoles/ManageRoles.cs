using core.Application.Common.Interfaces;
using core.Application.Identity.AccessControl.Dtos;
using core.Application.Identity.Common;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.SharedDataModel.Entities.IdentityAccess;
using wavio.SharedDataModel.Enums;
using wavio.Utilities.Auth.Audit;
using wavio.Utilities.Exceptions;
using wavio.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.AccessControl.Commands.ManageRoles;

// ── Requests ─────────────────────────────────────────────────────────────────
public sealed record CreateRoleRequest(string Code, string Name, string? Description, string ScopeType);
public sealed record UpdateRoleRequest(string Name, string? Description);
public sealed record CloneRoleRequest(string Code, string Name, string? Description);

public sealed record CreateRoleCommand(CreateRoleRequest Request, Guid? ActorId) : ICommand<RoleSummaryDto>;
public sealed record UpdateRoleCommand(Guid RoleId, UpdateRoleRequest Request, Guid? ActorId) : ICommand<bool>;
public sealed record DeleteRoleCommand(Guid RoleId, Guid? ActorId) : ICommand<bool>;
public sealed record CloneRoleCommand(Guid SourceRoleId, CloneRoleRequest Request, Guid? ActorId) : ICommand<RoleSummaryDto>;

// Default rank for UI-created custom roles: below the seeded brand managers (25–28),
// so they can be granted by those managers and never outrank them.
file static class RoleDefaults { public const short Priority = 50; }

file static class RoleValidation
{
    static readonly HashSet<string> Scopes = new(StringComparer.OrdinalIgnoreCase)
        { ScopeType.Tenant };

    public static string NormalizeCode(string? code)
    {
        var c = (code ?? "").Trim().ToLowerInvariant().Replace(' ', '_');
        if (c.Length is < 2 or > 50 || !System.Text.RegularExpressions.Regex.IsMatch(c, "^[a-z][a-z0-9_]*$"))
            throw new ValidationException(new Dictionary<string, string[]>
                { ["code"] = ["Use 2–50 chars: lowercase letters, digits, underscores; start with a letter."] });
        return c;
    }

    public static string Scope(string? scopeType)
    {
        var s = (scopeType ?? ScopeType.Tenant).Trim().ToLowerInvariant();
        if (!Scopes.Contains(s))
            throw new ValidationException(new Dictionary<string, string[]> { ["scopeType"] = ["Unsupported scope."] });
        return s;
    }

    public static void RequireName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100)
            throw new ValidationException(new Dictionary<string, string[]> { ["name"] = ["Name is required (≤100 chars)."] });
    }
}

// ── Create ───────────────────────────────────────────────────────────────────
public class CreateRoleCommandHandler : ICommandHandler<CreateRoleCommand, RoleSummaryDto>
{
    private readonly ICoreDbContext _db; private readonly ICurrentUser _user; private readonly IAuditWriter _audit;
    public CreateRoleCommandHandler(ICoreDbContext db, ICurrentUser user, IAuditWriter audit) { _db = db; _user = user; _audit = audit; }

    public async Task<RoleSummaryDto> HandleAsync(CreateRoleCommand cmd, CancellationToken ct)
    {
        var tenantId = _user.RequireTenantId();
        var code = RoleValidation.NormalizeCode(cmd.Request.Code);
        RoleValidation.RequireName(cmd.Request.Name);
        var scope = RoleValidation.Scope(cmd.Request.ScopeType);

        if (await _db.Roles.IgnoreQueryFilters().AnyAsync(r => r.TenantId == tenantId && r.Code == code && r.DeletedAt == null, ct))
            throw new ValidationException(new Dictionary<string, string[]> { ["code"] = ["A role with this code already exists."] });

        var now = DateTimeOffset.UtcNow;
        var role = new Role
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Code = code,
            Name = cmd.Request.Name.Trim(), Description = cmd.Request.Description?.Trim(),
            ScopeType = scope, IsSystem = false, IsAssignable = true,
            Priority = RoleDefaults.Priority, Status = "active",
            CreatedAt = now, UpdatedAt = now, CreatedBy = cmd.ActorId, UpdatedBy = cmd.ActorId,
        };
        _db.Roles.Add(role);
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("role.manage", "roles", role.Id,
            resourceDisplay: $"Created role '{role.Code}'",
            newValues: new { role.Code, role.Name, role.ScopeType }, ct: ct);

        return new RoleSummaryDto(role.Id, role.Code, role.Name, role.Description, role.ScopeType, false, 0, []);
    }
}

// ── Update (name/description; custom roles only) ──────────────────────────────
public class UpdateRoleCommandHandler : ICommandHandler<UpdateRoleCommand, bool>
{
    private readonly ICoreDbContext _db; private readonly ICurrentUser _user; private readonly IAuditWriter _audit;
    public UpdateRoleCommandHandler(ICoreDbContext db, ICurrentUser user, IAuditWriter audit) { _db = db; _user = user; _audit = audit; }

    public async Task<bool> HandleAsync(UpdateRoleCommand cmd, CancellationToken ct)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == cmd.RoleId && r.DeletedAt == null, ct);
        if (role is null) return false;
        if (role.IsSystem) throw new UnauthorizedAccessException("Built-in roles can't be edited.");
        if (!_user.IsPlatformAdmin && role.TenantId != _user.TenantId)
            throw new UnauthorizedAccessException("You can only edit roles in your own brand.");
        RoleValidation.RequireName(cmd.Request.Name);

        var before = new { role.Name, role.Description };
        role.Name = cmd.Request.Name.Trim();
        role.Description = cmd.Request.Description?.Trim();
        role.UpdatedAt = DateTimeOffset.UtcNow;
        role.UpdatedBy = cmd.ActorId;
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("role.manage", "roles", role.Id,
            resourceDisplay: $"Updated role '{role.Code}'",
            oldValues: before, newValues: new { role.Name, role.Description },
            changedFields: ["name", "description"], ct: ct);
        return true;
    }
}

// ── Delete (soft; custom roles with no members) ──────────────────────────────
public class DeleteRoleCommandHandler : ICommandHandler<DeleteRoleCommand, bool>
{
    private readonly ICoreDbContext _db; private readonly ICurrentUser _user; private readonly IAuditWriter _audit;
    public DeleteRoleCommandHandler(ICoreDbContext db, ICurrentUser user, IAuditWriter audit) { _db = db; _user = user; _audit = audit; }

    public async Task<bool> HandleAsync(DeleteRoleCommand cmd, CancellationToken ct)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == cmd.RoleId && r.DeletedAt == null, ct);
        if (role is null) return false;
        if (role.IsSystem) throw new UnauthorizedAccessException("Built-in roles can't be deleted.");
        if (!_user.IsPlatformAdmin && role.TenantId != _user.TenantId)
            throw new UnauthorizedAccessException("You can only delete roles in your own brand.");

        var members = await _db.UserScopeMemberships.CountAsync(m => m.RoleId == role.Id && m.RevokedAt == null, ct);
        if (members > 0)
            throw new ValidationException(new Dictionary<string, string[]>
                { ["role"] = [$"This role has {members} active member(s). Reassign them before deleting."] });

        // Drop its permission grants, then soft-delete the role.
        var rps = await _db.RolePermissions.Where(rp => rp.RoleId == role.Id).ToListAsync(ct);
        _db.RolePermissions.RemoveRange(rps);
        role.DeletedAt = DateTimeOffset.UtcNow;
        role.UpdatedBy = cmd.ActorId;
        await _db.SaveChangesAsync(ct);

        // Semantic audit: a soft-delete surfaces to the interceptor only as a "roles.updated"
        // (DeletedAt set); name it explicitly and record the dropped permission grants.
        await _audit.WriteAsync("role.manage", "roles", role.Id,
            resourceDisplay: $"Deleted role '{role.Code}'",
            oldValues: new { role.Code, role.Name, role.ScopeType, RemovedPermissionGrants = rps.Count }, ct: ct);
        return true;
    }
}

// ── Clone (new custom brand role + copied permission grants) ──────────────────
public class CloneRoleCommandHandler : ICommandHandler<CloneRoleCommand, RoleSummaryDto>
{
    private readonly ICoreDbContext _db; private readonly ICurrentUser _user; private readonly IAuditWriter _audit;
    public CloneRoleCommandHandler(ICoreDbContext db, ICurrentUser user, IAuditWriter audit) { _db = db; _user = user; _audit = audit; }

    public async Task<RoleSummaryDto> HandleAsync(CloneRoleCommand cmd, CancellationToken ct)
    {
        var source = await _db.Roles.FirstOrDefaultAsync(r => r.Id == cmd.SourceRoleId && r.DeletedAt == null, ct)
            ?? throw new ValidationException(new Dictionary<string, string[]> { ["sourceRoleId"] = ["Role not found."] });

        var tenantId = _user.RequireTenantId();
        var code = RoleValidation.NormalizeCode(cmd.Request.Code);
        RoleValidation.RequireName(cmd.Request.Name);
        if (await _db.Roles.IgnoreQueryFilters().AnyAsync(r => r.TenantId == tenantId && r.Code == code && r.DeletedAt == null, ct))
            throw new ValidationException(new Dictionary<string, string[]> { ["code"] = ["A role with this code already exists."] });

        var now = DateTimeOffset.UtcNow;
        var clone = new Role
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Code = code,
            Name = cmd.Request.Name.Trim(), Description = cmd.Request.Description?.Trim(),
            ScopeType = source.ScopeType, IsSystem = false, IsAssignable = true,
            Priority = RoleDefaults.Priority, Status = "active",
            CreatedAt = now, UpdatedAt = now, CreatedBy = cmd.ActorId, UpdatedBy = cmd.ActorId,
        };
        _db.Roles.Add(clone);

        var sourcePerms = await _db.RolePermissions.AsNoTracking()
            .Where(rp => rp.RoleId == source.Id)
            .Select(rp => new { rp.PermissionId, rp.Effect })
            .ToListAsync(ct);
        foreach (var sp in sourcePerms)
            _db.RolePermissions.Add(new RolePermission
            {
                Id = Guid.NewGuid(), RoleId = clone.Id, PermissionId = sp.PermissionId, Effect = sp.Effect,
                GrantedAt = now, GrantedBy = cmd.ActorId, CreatedAt = now, CreatedBy = cmd.ActorId,
            });

        await _db.SaveChangesAsync(ct);

        // Semantic audit: multi-entity action (new role + copied grants) collapsed to one named row.
        await _audit.WriteAsync("role.clone", "roles", clone.Id,
            resourceDisplay: $"Cloned '{source.Code}' → '{clone.Code}'",
            newValues: new { SourceRoleId = source.Id, source.Code, ClonedCode = clone.Code, clone.Name, CopiedPermissionGrants = sourcePerms.Count },
            ct: ct);

        return new RoleSummaryDto(clone.Id, clone.Code, clone.Name, clone.Description, clone.ScopeType, false, 0, []);
    }
}
