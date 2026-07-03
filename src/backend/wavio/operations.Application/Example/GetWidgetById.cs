using operations.Application.Common.Interfaces;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace operations.Application.Example;

public sealed record GetWidgetByIdQuery(Guid Id) : IQuery<WidgetDto?>;

public class GetWidgetByIdQueryHandler : IQueryHandler<GetWidgetByIdQuery, WidgetDto?>
{
    private readonly IOperationsDbContext _db;
    public GetWidgetByIdQueryHandler(IOperationsDbContext db) { _db = db; }

    public async Task<WidgetDto?> HandleAsync(GetWidgetByIdQuery q, CancellationToken ct) =>
        await _db.Widgets.AsNoTracking()
            .Where(w => w.Id == q.Id)
            .Select(w => new WidgetDto(w.Id, w.TenantId, w.Name, w.Description, w.Status, w.CreatedAt))
            .FirstOrDefaultAsync(ct);
}
