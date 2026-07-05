using WaIntel.Application.Quality.Queries.GetHealthReport;
using wavio.SharedDataModel.Entities.Quality;
using Xunit;

namespace WaIntel.Tests.Quality.Queries;

public class GetHealthReportHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid PhoneNumberId = Guid.NewGuid();

    [Fact]
    public async Task HandleAsync_MultipleSnapshotsForOneNumber_ReturnsOnlyTheLatestByPeriodStart()
    {
        await using var db = InMemoryWaIntelDbContext.Create(nameof(HandleAsync_MultipleSnapshotsForOneNumber_ReturnsOnlyTheLatestByPeriodStart));
        var older = new DateOnly(2026, 6, 15);
        var newer = new DateOnly(2026, 6, 22);

        db.HealthSnapshots.AddRange(
            Snapshot(older, messagesSent: 100),
            Snapshot(newer, messagesSent: 200));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetHealthReportHandler(db);
        var result = await handler.HandleAsync(new GetHealthReportQuery(TenantId, null), CancellationToken.None);

        var snapshot = Assert.Single(result.Snapshots);
        Assert.Equal(newer, snapshot.PeriodStart);
        Assert.Equal(200, snapshot.MessagesSent);
    }

    [Fact]
    public async Task HandleAsync_IncludesOnlyOpenIncidents_NotResolvedOnes()
    {
        await using var db = InMemoryWaIntelDbContext.Create(nameof(HandleAsync_IncludesOnlyOpenIncidents_NotResolvedOnes));
        db.GuardianIncidents.AddRange(
            Incident("quality_yellow", "open"),
            Incident("quality_red", "resolved"));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetHealthReportHandler(db);
        var result = await handler.HandleAsync(new GetHealthReportQuery(TenantId, null), CancellationToken.None);

        var incident = Assert.Single(result.OpenIncidents);
        Assert.Equal("quality_yellow", incident.IncidentType);
    }

    private static HealthSnapshot Snapshot(DateOnly periodStart, long messagesSent) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = TenantId,
        PhoneNumberId = PhoneNumberId,
        PeriodStart = periodStart,
        PeriodEnd = periodStart.AddDays(6),
        MessagesSent = messagesSent,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static GuardianIncident Incident(string incidentType, string status) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = TenantId,
        PhoneNumberId = PhoneNumberId,
        IncidentType = incidentType,
        Severity = "warning",
        Status = status,
        ThrottleAction = "marketing_50pct",
        OpenedAt = DateTimeOffset.UtcNow,
        ResolvedAt = status == "resolved" ? DateTimeOffset.UtcNow : null,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        Version = 1,
    };
}
