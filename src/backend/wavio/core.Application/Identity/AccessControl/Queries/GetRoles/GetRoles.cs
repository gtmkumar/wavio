using core.Application.Common.Interfaces;
using core.Application.Identity.Users.Dtos;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.AccessControl.Queries.GetRoles;

public sealed record GetRolesQuery(int Page = 1, int PageSize = 50) : IQuery<IReadOnlyList<RoleDto>>;

public class GetRolesQueryHandler : IQueryHandler<GetRolesQuery, IReadOnlyList<RoleDto>>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;
    public GetRolesQueryHandler(ICoreDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<IReadOnlyList<RoleDto>> HandleAsync(GetRolesQuery r, CancellationToken ct)
    {
        // Scope custom roles to the caller's tenant (system roles are global).
        var tenantId = _user.TryGetTenantId();

        var page = r.Page < 1 ? 1 : r.Page;
        var size = r.PageSize < 1 ? 50 : r.PageSize;

        return await _db.Roles.AsNoTracking()
            .Where(x => x.TenantId == null || tenantId == null || x.TenantId == tenantId)
            .OrderBy(x => x.Priority)
            .Skip((page - 1) * size).Take(size)
            .Select(x => new RoleDto(x.Id, x.Code, x.Name, x.ScopeType, x.IsSystem, x.Status))
            .ToListAsync(ct);
    }
}
