using WaAdmin.Application.Common.Interfaces;
using WaAdmin.Application.Templates.Dtos;
using wavio.Utilities.Common;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace WaAdmin.Application.Templates.Queries.GetTemplates;

/// <summary>GET /v1/templates — paginated, filterable by status/category/business account.
/// Tenant scoping comes from RLS (app.tenant_id), not an explicit filter here.</summary>
public sealed record GetTemplatesQuery(
    int Page = 1, int PageSize = 20, string? Status = null, string? Category = null, Guid? BusinessAccountId = null)
    : IQuery<PaginatedList<TemplateDto>>;

public sealed class GetTemplatesQueryHandler : IQueryHandler<GetTemplatesQuery, PaginatedList<TemplateDto>>
{
    // Security-review follow-up (S2, issue #16): an unclamped caller-supplied pageSize lets any
    // authenticated tenant force an arbitrarily large single-page fetch (resource abuse) — clamp
    // to a sane upper bound the same way a hostile or buggy client's request would otherwise slip
    // through unclamped `PageSize < 1 ? 20 : PageSize` only guarded the lower bound.
    private const int MaxPageSize = 200;

    private readonly IWaAdminDbContext _db;
    public GetTemplatesQueryHandler(IWaAdminDbContext db) => _db = db;

    public Task<PaginatedList<TemplateDto>> HandleAsync(GetTemplatesQuery query, CancellationToken cancellationToken)
    {
        var templates = _db.Templates.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(query.Status)) templates = templates.Where(t => t.Status == query.Status);
        if (!string.IsNullOrEmpty(query.Category)) templates = templates.Where(t => t.Category == query.Category);
        if (query.BusinessAccountId is { } baId) templates = templates.Where(t => t.BusinessAccountId == baId);

        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize <= 0 ? 20 : query.PageSize, 1, MaxPageSize);

        // Current-version content is intentionally not joined into the list projection (keeps the
        // list query light); GET /v1/templates/{id} returns the full detail with current version.
        // Built as a plain `new TemplateDto(...)` (not the TemplateMapper extension method) so EF
        // can translate the projection into SQL — extension method calls are not translatable.
        return PaginatedList<TemplateDto>.CreateAsync(
            templates.OrderByDescending(t => t.CreatedAt).Select(t => new TemplateDto(
                t.Id, t.BusinessAccountId, t.Name, t.Language, t.Category, t.MetaTemplateId, t.Status,
                t.CurrentVersionId, t.PausedUntil, t.PauseCount, t.CreatedAt, t.UpdatedAt, null)),
            page, pageSize, cancellationToken);
    }
}
