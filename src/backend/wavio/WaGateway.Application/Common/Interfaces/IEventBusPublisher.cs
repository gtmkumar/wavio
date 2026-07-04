using WaPlatform.Contracts.IntegrationEvents;

namespace WaGateway.Application.Common.Interfaces;

/// <summary>
/// Publishes a normalized <see cref="IntegrationEvent"/> onto the platform bus. Implemented over
/// RabbitMQ in WaGateway.Infrastructure (exchange <c>wavio.events</c>, topic, routing key =
/// <see cref="IntegrationEvent.EventName"/> — e.g. <c>wa.message.send_failed.v1</c>). Same
/// contract as WaIngest's/WaIntel's publisher interface of the same name — each service owns its
/// own copy rather than sharing a cross-service project reference (bounded-context isolation).
/// </summary>
public interface IEventBusPublisher
{
    Task PublishAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken);
}
