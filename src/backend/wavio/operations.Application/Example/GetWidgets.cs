using operations.Application.Common.Interfaces;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.Utilities.Common;
using wavio.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace operations.Application.Example;

public sealed record GetWidgetsQuery(int Page = 1, int PageSize = 20) : IQuery<PaginatedList<WidgetDto>>;

public class GetWidgetsQueryHandler : IQueryHandler<GetWidgetsQuery, PaginatedList<WidgetDto>>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    public GetWidgetsQueryHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<WidgetDto>> HandleAsync(GetWidgetsQuery q, CancellationToken ct)
    {
        var tenantId = _user.TryGetTenantId();

        var query = _db.Widgets.AsNoTracking()
            .Where(w => tenantId == null || w.TenantId == tenantId)
            .OrderByDescending(w => w.CreatedAt)
            .Select(w => new WidgetDto(w.Id, w.TenantId, w.Name, w.Description, w.Status, w.CreatedAt));

        return PaginatedList<WidgetDto>.CreateAsync(query, q.Page < 1 ? 1 : q.Page, q.PageSize < 1 ? 20 : q.PageSize, ct);
    }
}
