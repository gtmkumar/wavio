using WaIntel.Application.Common.Interfaces;
using WaPlatform.Contracts.IntegrationEvents;

namespace WaIntel.Tests.Quality;

/// <summary>Hand-rolled fake (not a mock) recording every published event, so tests can assert
/// exactly what was published without a mocking framework — same "prefer real objects/fakes"
/// convention the project favors over over-mocking.</summary>
public sealed class FakeEventBusPublisher : IEventBusPublisher
{
    public List<IntegrationEvent> Published { get; } = [];

    public Task PublishAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        Published.Add(integrationEvent);
        return Task.CompletedTask;
    }
}
