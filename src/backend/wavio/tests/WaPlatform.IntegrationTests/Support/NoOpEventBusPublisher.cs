using WaGateway.Application.Common.Interfaces;
using WaPlatform.Contracts.IntegrationEvents;

namespace WaPlatform.IntegrationTests.Support;

/// <summary>
/// No RabbitMQ broker in this test tier (issue #46 scoped this to Postgres-shaped code only —
/// RabbitMQ.Client's own connection-manager tests already exist elsewhere, e.g.
/// tests/WaAdmin.Tests/Messaging/RabbitMqConnectionManagerTests.cs). Every dispatcher path this
/// project exercises publishes only on FAILURE branches; the fenced-write test drives the SUCCESS
/// path, so this is never invoked there, but it is wired so a future test hitting a failure branch
/// doesn't NullReferenceException resolving the publisher instead of getting a useful assertion.
/// </summary>
public sealed class NoOpEventBusPublisher : IEventBusPublisher
{
    public Task PublishAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
