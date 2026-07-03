using WaPlatform.Contracts.IntegrationEvents;

namespace WaIntel.Application.Common.Interfaces;

/// <summary>
/// Publishes a normalized <see cref="IntegrationEvent"/> onto the platform bus. Implemented over
/// RabbitMQ in WaIntel.Infrastructure (exchange <c>wavio.events</c>, topic, routing key =
/// <see cref="IntegrationEvent.EventName"/> — e.g. <c>wa.window.closing.v1</c>). Same contract
/// as WaIngest.Application's publisher interface of the same name — each service owns its own
/// copy rather than sharing a cross-service project reference (bounded-context isolation).
/// </summary>
public interface IEventBusPublisher
{
    Task PublishAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken);
}
