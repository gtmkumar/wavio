using WaAdmin.Application.Common.Interfaces;
using WaAdmin.Application.Consent.Dtos;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace WaAdmin.Application.RetentionPolicies.Queries.GetRetentionPolicies;

public sealed class GetRetentionPoliciesQueryHandler
    : IQueryHandler<GetRetentionPoliciesQuery, IReadOnlyList<RetentionPolicyDto>>
{
    private readonly IWaAdminDbContext _db;

    public GetRetentionPoliciesQueryHandler(IWaAdminDbContext db) => _db = db;

    public async Task<IReadOnlyList<RetentionPolicyDto>> HandleAsync(
        GetRetentionPoliciesQuery query, CancellationToken cancellationToken)
    {
        // RLS already restricts visibility to (tenant_id IS NULL OR tenant_id = current tenant);
        // the explicit filter here is defense in depth, matching SendMessageHandler's convention.
        var rows = await _db.RetentionPolicies.AsNoTracking()
            .Where(p => p.TenantId == null || p.TenantId == query.TenantId)
            .ToListAsync(cancellationToken);

        // Effective row per data class: prefer this tenant's override over the platform default.
        var byDataClass = rows
            .GroupBy(p => p.DataClass)
            .Select(g => g.OrderByDescending(p => p.TenantId != null).First());

        return [.. byDataClass
            .OrderBy(p => p.DataClass, StringComparer.Ordinal)
            .Select(p => new RetentionPolicyDto(
                p.Id, p.TenantId, p.DataClass, p.RetentionDays, p.Basis, p.Enabled, p.UpdatedAt))];
    }
}
