using WaIntel.Application.Quality.Commands.SimulateQualityEvent;
using WaIntel.Application.Quality.Logic;
using wavio.SharedDataModel.Entities.Waba;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace WaIntel.Tests.Quality.Commands;

public class SimulateQualityEventHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid PhoneNumberId = Guid.NewGuid();

    private static IHostEnvironment NonProdEnvironment()
    {
        var mock = new Mock<IHostEnvironment>();
        mock.Setup(e => e.EnvironmentName).Returns(Environments.Development);
        return mock.Object;
    }

    private static IHostEnvironment ProductionEnvironment()
    {
        var mock = new Mock<IHostEnvironment>();
        mock.Setup(e => e.EnvironmentName).Returns(Environments.Production);
        return mock.Object;
    }

    private static async Task<InMemoryWaIntelDbContext> SeedPhoneNumberAsync(string testName)
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
            QualityRating = "GREEN",
            MessagingTier = "TIER_1K",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1,
        });
        await db.SaveChangesAsync(CancellationToken.None);
        return db;
    }

    [Fact]
    public async Task HandleAsync_Refuses_InProduction()
    {
        await using var db = await SeedPhoneNumberAsync(nameof(HandleAsync_Refuses_InProduction));
        var handler = new SimulateQualityEventHandler(db, new FakeEventBusPublisher(), ProductionEnvironment());

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(
            new SimulateQualityEventCommand(TenantId, PhoneNumberId, "YELLOW", null), CancellationToken.None));

        Assert.Empty(db.NumberQualityEvents);
    }

    [Fact]
    public async Task HandleAsync_SimulatedRatingOnly_RecordsEventSourceSimulated()
    {
        await using var db = await SeedPhoneNumberAsync(nameof(HandleAsync_SimulatedRatingOnly_RecordsEventSourceSimulated));
        var handler = new SimulateQualityEventHandler(db, new FakeEventBusPublisher(), NonProdEnvironment());

        var result = await handler.HandleAsync(
            new SimulateQualityEventCommand(TenantId, PhoneNumberId, "RED", null), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(GuardianRules.IncidentQualityRed, result!.IncidentType);
        var qualityEvent = Assert.Single(db.NumberQualityEvents);
        Assert.Equal("simulated", qualityEvent.EventSource);
    }

    [Fact]
    public async Task HandleAsync_SimulatedTierOnly_RecordsTierEventAndLeavesRatingUntouched()
    {
        await using var db = await SeedPhoneNumberAsync(nameof(HandleAsync_SimulatedTierOnly_RecordsTierEventAndLeavesRatingUntouched));
        var handler = new SimulateQualityEventHandler(db, new FakeEventBusPublisher(), NonProdEnvironment());

        await handler.HandleAsync(new SimulateQualityEventCommand(TenantId, PhoneNumberId, null, "TIER_250"), CancellationToken.None);

        Assert.Empty(db.NumberQualityEvents);
        var tierEvent = Assert.Single(db.MessagingTierEvents);
        Assert.Equal("simulated", tierEvent.EventSource);
    }

    [Fact]
    public async Task HandleAsync_BothRatingAndTier_AppliesBoth()
    {
        await using var db = await SeedPhoneNumberAsync(nameof(HandleAsync_BothRatingAndTier_AppliesBoth));
        var handler = new SimulateQualityEventHandler(db, new FakeEventBusPublisher(), NonProdEnvironment());

        await handler.HandleAsync(new SimulateQualityEventCommand(TenantId, PhoneNumberId, "YELLOW", "TIER_250"), CancellationToken.None);

        Assert.Single(db.NumberQualityEvents);
        Assert.Single(db.MessagingTierEvents);
    }
}
