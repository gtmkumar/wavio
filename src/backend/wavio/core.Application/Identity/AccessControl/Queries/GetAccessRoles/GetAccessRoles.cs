using core.Application.Common.Interfaces;
using core.Application.Identity.AccessControl.Dtos;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.AccessControl.Queries.GetAccessRoles;

public sealed record GetAccessRolesQuery : IQuery<AccessRolesDto>;

public class GetAccessRolesQueryHandler : IQueryHandler<GetAccessRolesQuery, AccessRolesDto>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;
    public GetAccessRolesQueryHandler(ICoreDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<AccessRolesDto> HandleAsync(GetAccessRolesQuery q, CancellationToken ct)
    {
        var matrix = await ModuleMatrix.LoadAsync(_db, ct);

        // System roles (TenantId == null) are global; custom roles are tenant-scoped and must not
        // leak across tenants. Scope to the caller's active tenant (X-Tenant-Id / JWT); when no
        // tenant context is resolvable (platform admin, no selection) fall back to the full set.
        var tenantId = _user.TryGetTenantId();

        var roles = await _db.Roles.AsNoTracking()
            .Where(r => r.DeletedAt == null && r.Status == "active"
                && (r.TenantId == null || tenantId == null || r.TenantId == tenantId))
            .Select(r => new
            {
                r.Id,
                r.Code,
                r.Name,
                r.Description,
                r.ScopeType,
                r.IsSystem,
                r.Priority,
                PermCodes = r.RolePermissions.Select(rp => rp.Permission.Code).ToList(),
                MemberCount = r.UserScopeMemberships.Count(m => m.RevokedAt == null),
            })
            .ToListAsync(ct);

        var summaries = roles.Select(r =>
        {
            var onCells = new HashSet<string>();
            foreach (var code in r.PermCodes)
                foreach (var cell in matrix.CellsFor(PermissionMatrix.Module(code), PermissionMatrix.Action(code)))
                    onCells.Add(cell);
            return new
            {
                r.ScopeType,
                Dto = new RoleSummaryDto(r.Id, r.Code, r.Name, r.Description, r.ScopeType, r.IsSystem,
                    r.MemberCount, onCells.OrderBy(c => c).ToList()),
                r.Priority,
            };
        }).ToList();

        RoleGroupDto Group(string tier, string label) =>
            new(tier, label, summaries
                .Where(s => AccessHelpers.Tier(s.ScopeType) == tier)
                .OrderByDescending(s => s.Priority)
                .ThenBy(s => s.Dto.Name)
                .Select(s => s.Dto).ToList());

        var groups = new List<RoleGroupDto> { Group("platform", "Platform"), Group("tenant", "Tenant") };

        // Build cellKey → permission codes (the fan-out), so the UI can show what a checkbox grants.
        var allCodes = await _db.Permissions.AsNoTracking().Select(p => p.Code).ToListAsync(ct);
        var cells = new Dictionary<string, List<string>>();
        foreach (var code in allCodes)
            foreach (var cell in matrix.CellsFor(PermissionMatrix.Module(code), PermissionMatrix.Action(code)))
                (cells.TryGetValue(cell, out var l) ? l : cells[cell] = []).Add(code);
        var cellMap = cells.ToDictionary(
            kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value.OrderBy(c => c).ToList());

        return new AccessRolesDto(
            matrix.Rows.Select(m => new MatrixModuleDto(m.Key, m.Label)).ToList(),
            PermissionMatrix.Actions.ToList(),
            groups,
            cellMap);
    }
}
