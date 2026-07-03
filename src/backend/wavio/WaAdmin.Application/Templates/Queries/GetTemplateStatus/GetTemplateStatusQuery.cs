using WaAdmin.Application.Common.Interfaces;
using WaAdmin.Application.Templates.Dtos;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace WaAdmin.Application.Templates.Queries.GetTemplateStatus;

/// <summary>GET /v1/templates/{id}/status (spec §7.1) — current status plus the full
/// transition history from templates.template_status_events, newest first.</summary>
public sealed record GetTemplateStatusQuery(Guid TemplateId) : IQuery<TemplateStatusDto?>;

public sealed class GetTemplateStatusQueryHandler : IQueryHandler<GetTemplateStatusQuery, TemplateStatusDto?>
{
    private readonly IWaAdminDbContext _db;
    public GetTemplateStatusQueryHandler(IWaAdminDbContext db) => _db = db;

    public async Task<TemplateStatusDto?> HandleAsync(GetTemplateStatusQuery query, CancellationToken cancellationToken)
    {
        var template = await _db.Templates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == query.TemplateId, cancellationToken);
        if (template is null) return null;

        var history = await _db.TemplateStatusEvents.AsNoTracking()
            .Where(e => e.TemplateId == query.TemplateId)
            .OrderByDescending(e => e.OccurredAt)
            .Select(e => new TemplateStatusEventDto(e.Id, e.OldStatus, e.NewStatus, e.Reason, e.OccurredAt))
            .ToListAsync(cancellationToken);

        return new TemplateStatusDto(
            template.Id, template.Status, template.MetaTemplateId,
            template.PausedUntil, template.PauseCount, history);
    }
}
