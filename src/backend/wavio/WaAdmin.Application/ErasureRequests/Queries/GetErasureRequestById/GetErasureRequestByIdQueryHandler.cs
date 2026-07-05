using WaAdmin.Application.Common.Interfaces;
using WaAdmin.Application.Consent.Dtos;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace WaAdmin.Application.ErasureRequests.Queries.GetErasureRequestById;

public sealed class GetErasureRequestByIdQueryHandler : IQueryHandler<GetErasureRequestByIdQuery, ErasureRequestDto?>
{
    private readonly IWaAdminDbContext _db;

    public GetErasureRequestByIdQueryHandler(IWaAdminDbContext db) => _db = db;

    public async Task<ErasureRequestDto?> HandleAsync(GetErasureRequestByIdQuery query, CancellationToken cancellationToken)
    {
        var r = await _db.ErasureRequests.AsNoTracking()
            .FirstOrDefaultAsync(e => e.TenantId == query.TenantId && e.Id == query.Id, cancellationToken);

        return r is null ? null
            : new ErasureRequestDto(r.Id, r.WaId, r.RequestType, r.Status, r.Reason, r.ContentErasedAt, r.ExportRef, r.CompletedAt, r.CreatedAt);
    }
}
