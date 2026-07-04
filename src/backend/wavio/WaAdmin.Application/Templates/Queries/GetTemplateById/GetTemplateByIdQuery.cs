using WaAdmin.Application.Common.Interfaces;
using WaAdmin.Application.Templates.Dtos;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace WaAdmin.Application.Templates.Queries.GetTemplateById;

/// <summary>GET /v1/templates/{id} — full detail including the current version's content.</summary>
public sealed record GetTemplateByIdQuery(Guid TemplateId) : IQuery<TemplateDto?>;

public sealed class GetTemplateByIdQueryHandler : IQueryHandler<GetTemplateByIdQuery, TemplateDto?>
{
    private readonly IWaAdminDbContext _db;
    public GetTemplateByIdQueryHandler(IWaAdminDbContext db) => _db = db;

    public async Task<TemplateDto?> HandleAsync(GetTemplateByIdQuery query, CancellationToken cancellationToken)
    {
        var template = await _db.Templates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == query.TemplateId, cancellationToken);
        if (template is null) return null;

        var currentVersion = template.CurrentVersionId is null
            ? null
            : await _db.TemplateVersions.AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == template.CurrentVersionId, cancellationToken);

        return template.ToDto(currentVersion);
    }
}
