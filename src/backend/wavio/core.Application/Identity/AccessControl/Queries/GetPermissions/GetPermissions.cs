using core.Application.Common.Interfaces;
using core.Application.Identity.Users.Dtos;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.AccessControl.Queries.GetPermissions;

public sealed record GetPermissionsQuery(string? Module = null) : IQuery<IReadOnlyList<PermissionDto>>;

public class GetPermissionsQueryHandler : IQueryHandler<GetPermissionsQuery, IReadOnlyList<PermissionDto>>
{
    private readonly ICoreDbContext _db;
    public GetPermissionsQueryHandler(ICoreDbContext db) => _db = db;

    public async Task<IReadOnlyList<PermissionDto>> HandleAsync(GetPermissionsQuery r, CancellationToken ct)
    {
        var q = _db.Permissions.AsNoTracking().Where(p => p.Status == "active");
        if (!string.IsNullOrEmpty(r.Module)) q = q.Where(p => p.Module == r.Module);
        return await q.OrderBy(p => p.Code).Select(p => new PermissionDto(p.Id, p.Code, p.Module, p.Action, p.Name, p.RiskLevel)).ToListAsync(ct);
    }
}
