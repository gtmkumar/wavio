using WaAdmin.Application.Common.Interfaces;
using WaAdmin.Application.Waba.Dtos;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace WaAdmin.Application.Waba.Queries.GetPhoneNumbers;

/// <summary>GET /v1/waba/phone-numbers — the tenant's sender numbers for console pickers.
/// Tenant scoping comes from RLS (app.tenant_id), not an explicit filter here.</summary>
public sealed record GetPhoneNumbersQuery : IQuery<IReadOnlyList<PhoneNumberSummaryDto>>;

public sealed class GetPhoneNumbersQueryHandler
    : IQueryHandler<GetPhoneNumbersQuery, IReadOnlyList<PhoneNumberSummaryDto>>
{
    // A tenant has at most a handful of numbers; the cap is a resource-abuse guard consistent
    // with GetTemplatesQueryHandler's MaxPageSize rationale (security review S2), not pagination.
    private const int MaxRows = 200;

    private readonly IWaAdminDbContext _db;
    public GetPhoneNumbersQueryHandler(IWaAdminDbContext db) => _db = db;

    public async Task<IReadOnlyList<PhoneNumberSummaryDto>> HandleAsync(
        GetPhoneNumbersQuery query, CancellationToken cancellationToken)
    {
        return await _db.WabaPhoneNumbers.AsNoTracking()
            .OrderBy(p => p.DisplayPhoneNumber)
            .Take(MaxRows)
            .Select(p => new PhoneNumberSummaryDto(
                p.Id, p.BusinessAccountId, p.DisplayPhoneNumber, p.Status,
                p.QualityRating, p.MessagingTier))
            .ToListAsync(cancellationToken);
    }
}
