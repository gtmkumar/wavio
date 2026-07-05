using WaIntel.Application.Quality.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaIntel.Application.Quality.Queries.GetHealthReport;

/// <summary>Weekly per-number health report (spec §4.6): latest snapshot per number plus any
/// currently open Guardian incidents. <paramref name="PhoneNumberId"/> null = every number for
/// the tenant.</summary>
public sealed record GetHealthReportQuery(Guid TenantId, Guid? PhoneNumberId) : IQuery<HealthReportDto>;
