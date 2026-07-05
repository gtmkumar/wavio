using WaIntel.Application.Common.Interfaces;
using WaIntel.Application.Quality.Dtos;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.SharedDataModel.Entities.Quality;
using Microsoft.EntityFrameworkCore;

namespace WaIntel.Application.Quality.Queries.GetHealthReport;

/// <summary>
/// Reads the latest <c>health_snapshots</c> row per number plus any open
/// <c>guardian_incidents</c>. The "latest per number" reduction is done client-side after a single
/// ordered fetch rather than a GroupBy/window-function query — snapshot volume per tenant is small
/// (one row per number per week) and this keeps the LINQ trivially provider-portable.
/// </summary>
public sealed class GetHealthReportHandler : IQueryHandler<GetHealthReportQuery, HealthReportDto>
{
    private readonly IWaIntelDbContext _db;

    public GetHealthReportHandler(IWaIntelDbContext db) => _db = db;

    public async Task<HealthReportDto> HandleAsync(GetHealthReportQuery query, CancellationToken cancellationToken)
    {
        var snapshotsQuery = _db.HealthSnapshots.AsNoTracking().Where(s => s.TenantId == query.TenantId);
        var incidentsQuery = _db.GuardianIncidents.AsNoTracking()
            .Where(i => i.TenantId == query.TenantId && i.Status != "resolved");

        if (query.PhoneNumberId.HasValue)
        {
            snapshotsQuery = snapshotsQuery.Where(s => s.PhoneNumberId == query.PhoneNumberId.Value);
            incidentsQuery = incidentsQuery.Where(i => i.PhoneNumberId == query.PhoneNumberId.Value);
        }

        var allSnapshots = await snapshotsQuery.OrderByDescending(s => s.PeriodStart).ToListAsync(cancellationToken);
        var latestPerNumber = allSnapshots.GroupBy(s => s.PhoneNumberId).Select(g => g.First());

        var incidents = await incidentsQuery.OrderByDescending(i => i.OpenedAt).ToListAsync(cancellationToken);

        return new HealthReportDto(
            latestPerNumber.Select(ToDto).ToList(),
            incidents.Select(ToIncidentDto).ToList());
    }

    private static HealthSnapshotDto ToDto(HealthSnapshot s) => new(
        s.PhoneNumberId, s.PeriodStart, s.PeriodEnd, s.DeliveryRate, s.ReadRate, s.BlockProxyRate,
        s.QualityRating, s.MessagingTier, s.TierHeadroom, s.MessagesSent, s.MessagesDelivered,
        s.MessagesRead, s.MessagesFailed);

    private static GuardianIncidentDto ToIncidentDto(GuardianIncident i) => new(
        i.Id, i.PhoneNumberId, i.IncidentType, i.Severity, i.Status, i.ThrottleAction, i.TriggerRating,
        i.OpenedAt, i.ResolvedAt);
}
