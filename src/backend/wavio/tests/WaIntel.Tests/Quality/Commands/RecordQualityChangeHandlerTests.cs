using WaIntel.Application.Quality.Commands.RecordQualityChange;
using WaIntel.Application.Quality.Logic;
using WaIntel.Tests.Quality;
using wavio.SharedDataModel.Entities.Quality;
using wavio.SharedDataModel.Entities.Waba;
using WaPlatform.Contracts.IntegrationEvents.V1;
using Xunit;

namespace WaIntel.Tests.Quality.Commands;

public class RecordQualityChangeHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid PhoneNumberId = Guid.NewGuid();

    private static async Task<InMemoryWaIntelDbContext> SeedPhoneNumberAsync(string testName, string? currentRating)
    {
        var db = InMemoryWaIntelDbContext.Create(testName);
        db.WabaPhoneNumbers.Add(new WabaPhoneNumber
        {
            Id = PhoneNumberId,
            TenantId = TenantId,
            BusinessAccountId = Guid.NewGuid(),
            MetaPhoneNumberId = "1234567890",
            DisplayPhoneNumber = "+1 234 567 890",
            Status = "CONNECTED",
            QualityRating = currentRating,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1,
        });
        await db.SaveChangesAsync(CancellationToken.None);
        return db;
    }

    [Fact]
    public async Task HandleAsync_GreenToYellow_OpensQualityYellowIncidentWithHalvedThrottle()
    {
        await using var db = await SeedPhoneNumberAsync(nameof(HandleAsync_GreenToYellow_OpensQualityYellowIncidentWithHalvedThrottle), "GREEN");
        var publisher = new FakeEventBusPublisher();
        var handler = new RecordQualityChangeHandler(db, publisher);

        var result = await handler.HandleAsync(
            new RecordQualityChangeCommand(TenantId, PhoneNumberId, "waba-1", "YELLOW", "webhook", null),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(GuardianRules.IncidentQualityYellow, result!.IncidentType);
        Assert.Equal(GuardianRules.Throttle50Pct, result.ThrottleAction);
        Assert.Equal("open", result.Status);

        var phoneNumber = db.WabaPhoneNumbers.Single(p => p.Id == PhoneNumberId);
        Assert.Equal("YELLOW", phoneNumber.QualityRating);

        var qualityEvent = Assert.Single(db.NumberQualityEvents);
        Assert.Equal("green", qualityEvent.OldRating);
        Assert.Equal("yellow", qualityEvent.NewRating);

        Assert.Single(publisher.Published.OfType<GuardianIncidentOpenedV1>());
    }

    [Fact]
    public async Task HandleAsync_YellowToRed_FreezesMarketingAndResolvesTheYellowIncident()
    {
        await using var db = await SeedPhoneNumberAsync(nameof(HandleAsync_YellowToRed_FreezesMarketingAndResolvesTheYellowIncident), "YELLOW");
        db.GuardianIncidents.Add(new GuardianIncident
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            PhoneNumberId = PhoneNumberId,
            IncidentType = GuardianRules.IncidentQualityYellow,
            Severity = "warning",
            Status = "open",
            ThrottleAction = GuardianRules.Throttle50Pct,
            TriggerRating = "yellow",
            OpenedAt = DateTimeOffset.UtcNow.AddHours(-1),
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            Version = 1,
        });
        await db.SaveChangesAsync(CancellationToken.None);

        var publisher = new FakeEventBusPublisher();
        var handler = new RecordQualityChangeHandler(db, publisher);

        var result = await handler.HandleAsync(
            new RecordQualityChangeCommand(TenantId, PhoneNumberId, "waba-1", "RED", "webhook", null),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(GuardianRules.IncidentQualityRed, result!.IncidentType);
        Assert.Equal(GuardianRules.ThrottleFrozen, result.ThrottleAction);

        var yellowIncident = db.GuardianIncidents.Single(i => i.IncidentType == GuardianRules.IncidentQualityYellow);
        Assert.Equal("resolved", yellowIncident.Status);
        Assert.NotNull(yellowIncident.ResolvedAt);

        Assert.Single(publisher.Published.OfType<GuardianIncidentResolvedV1>());
        Assert.Single(publisher.Published.OfType<GuardianIncidentOpenedV1>());
    }

    [Fact]
    public async Task HandleAsync_RecoveryToGreen_ResolvesOpenIncidentAndDoesNotOpenANewOne()
    {
        await using var db = await SeedPhoneNumberAsync(nameof(HandleAsync_RecoveryToGreen_ResolvesOpenIncidentAndDoesNotOpenANewOne), "RED");
        db.GuardianIncidents.Add(new GuardianIncident
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            PhoneNumberId = PhoneNumberId,
            IncidentType = GuardianRules.IncidentQualityRed,
            Severity = "critical",
            Status = "open",
            ThrottleAction = GuardianRules.ThrottleFrozen,
            TriggerRating = "red",
            OpenedAt = DateTimeOffset.UtcNow.AddHours(-2),
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-2),
            UpdatedAt = DateTimeOffset.UtcNow.AddHours(-2),
            Version = 1,
        });
        await db.SaveChangesAsync(CancellationToken.None);

        var publisher = new FakeEventBusPublisher();
        var handler = new RecordQualityChangeHandler(db, publisher);

        var result = await handler.HandleAsync(
            new RecordQualityChangeCommand(TenantId, PhoneNumberId, "waba-1", "GREEN", "webhook", null),
            CancellationToken.None);

        Assert.Null(result);
        var incident = db.GuardianIncidents.Single();
        Assert.Equal("resolved", incident.Status);
        Assert.Single(publisher.Published.OfType<GuardianIncidentResolvedV1>());
        Assert.Empty(publisher.Published.OfType<GuardianIncidentOpenedV1>());
    }

    [Fact]
    public async Task HandleAsync_RedeliveredSameRating_IsANoOpAndDoesNotDuplicateEventsOrIncidents()
    {
        await using var db = await SeedPhoneNumberAsync(nameof(HandleAsync_RedeliveredSameRating_IsANoOpAndDoesNotDuplicateEventsOrIncidents), "GREEN");
        var publisher = new FakeEventBusPublisher();
        var handler = new RecordQualityChangeHandler(db, publisher);

        // First call actually changes the rating and opens an incident.
        await handler.HandleAsync(new RecordQualityChangeCommand(TenantId, PhoneNumberId, "waba-1", "YELLOW", "webhook", null), CancellationToken.None);

        // Redelivery of the SAME webhook (same rating) — must not add a second event row or a
        // second incident.
        var result = await handler.HandleAsync(
            new RecordQualityChangeCommand(TenantId, PhoneNumberId, "waba-1", "YELLOW", "webhook", null),
            CancellationToken.None);

        Assert.NotNull(result); // still returns the existing open incident for context
        Assert.Single(db.NumberQualityEvents); // only the first (real) transition was logged
        Assert.Single(db.GuardianIncidents); // only one incident, not duplicated on redelivery
    }
}
