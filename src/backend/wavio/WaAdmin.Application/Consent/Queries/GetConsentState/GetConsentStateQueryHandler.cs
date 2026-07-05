using WaAdmin.Application.Common.Interfaces;
using WaAdmin.Application.Consent.Dtos;
using WaAdmin.Application.Consent.Logic;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace WaAdmin.Application.Consent.Queries.GetConsentState;

public sealed class GetConsentStateQueryHandler : IQueryHandler<GetConsentStateQuery, ConsentStateDto>
{
    private readonly IWaAdminDbContext _db;

    public GetConsentStateQueryHandler(IWaAdminDbContext db) => _db = db;

    public async Task<ConsentStateDto> HandleAsync(GetConsentStateQuery query, CancellationToken cancellationToken)
    {
        var optIns = await _db.OptInEvents.AsNoTracking()
            .Where(o => o.TenantId == query.TenantId && o.WaId == query.WaId)
            .Select(o => new { o.Purpose, o.OccurredAt })
            .ToListAsync(cancellationToken);

        var optOuts = await _db.OptOutEvents.AsNoTracking()
            .Where(o => o.TenantId == query.TenantId && o.WaId == query.WaId)
            .Select(o => new { o.Scope, o.OccurredAt })
            .ToListAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var suppressed = await _db.SuppressionListEntries.AsNoTracking().AnyAsync(
            s => s.TenantId == query.TenantId && s.WaId == query.WaId
              && (s.ExpiresAt == null || s.ExpiresAt > now),
            cancellationToken);

        var purposeStates = ConsentStateResolver.Resolve(
            [.. optIns.Select(o => (o.Purpose, o.OccurredAt))],
            [.. optOuts.Select(o => (o.Scope, o.OccurredAt))]);

        return new ConsentStateDto(
            query.WaId,
            suppressed,
            [.. purposeStates.Select(p => new ConsentPurposeStateDto(p.Purpose, p.OptedIn, p.LastOptInAt, p.LastOptOutAt))]);
    }
}
