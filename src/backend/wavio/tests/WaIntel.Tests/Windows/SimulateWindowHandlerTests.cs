using WaIntel.Application.Windows.Commands.SimulateWindow;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace WaIntel.Tests.Windows;

public class SimulateWindowHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid PhoneNumberId = Guid.NewGuid();
    private const string UserWaId = "919876543210";

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

    [Fact]
    public async Task Refuses_to_run_in_Production_even_if_somehow_invoked()
    {
        await using var db = InMemoryWaIntelDbContext.Create(nameof(Refuses_to_run_in_Production_even_if_somehow_invoked));
        var handler = new SimulateWindowHandler(db, ProductionEnvironment());

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(
            new SimulateWindowCommand(TenantId, PhoneNumberId, UserWaId, "organic", DateTimeOffset.UtcNow.AddHours(1), null),
            CancellationToken.None));

        Assert.Empty(db.ConversationWindows);
    }

    [Fact]
    public async Task Fabricates_the_exact_expiry_given_with_no_24h72h_calculation()
    {
        await using var db = InMemoryWaIntelDbContext.Create(nameof(Fabricates_the_exact_expiry_given_with_no_24h72h_calculation));
        var handler = new SimulateWindowHandler(db, NonProdEnvironment());
        var arbitraryExpiry = DateTimeOffset.UtcNow.AddMinutes(17); // deliberately not a round +24h/+72h offset

        var result = await handler.HandleAsync(
            new SimulateWindowCommand(TenantId, PhoneNumberId, UserWaId, "ctwa", null, arbitraryExpiry),
            CancellationToken.None);

        Assert.Equal(arbitraryExpiry, result.CtwaExpiresAt);
        Assert.True(db.ConversationWindows.Single().IsSimulated);
    }

    [Fact]
    public async Task Records_a_simulated_WindowEvent_for_QA_traceability()
    {
        await using var db = InMemoryWaIntelDbContext.Create(nameof(Records_a_simulated_WindowEvent_for_QA_traceability));
        var handler = new SimulateWindowHandler(db, NonProdEnvironment());

        await handler.HandleAsync(
            new SimulateWindowCommand(TenantId, PhoneNumberId, UserWaId, "organic", DateTimeOffset.UtcNow.AddHours(1), null),
            CancellationToken.None);

        var windowEvent = Assert.Single(db.WindowEvents);
        Assert.Equal("simulated", windowEvent.EventType);
    }
}
