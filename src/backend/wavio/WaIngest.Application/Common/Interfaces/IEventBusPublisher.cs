using WaPlatform.Contracts.IntegrationEvents;

namespace WaIngest.Application.Common.Interfaces;

/// <summary>
/// Publishes a normalized <see cref="IntegrationEvent"/> onto the platform bus. Implemented over
/// RabbitMQ in WaIngest.Infrastructure (exchange <c>wavio.events</c>, topic, routing key =
/// <see cref="IntegrationEvent.EventName"/> — e.g. <c>wa.message.received.v1</c>).
///
/// Degraded-mode contract (spec §8: "ingest never drops"): implementations MUST throw on
/// failure (never swallow) so <c>WebhookProcessor</c> can mark the source raw_webhooks row
/// 'failed' for the replay tool to pick up later — they must NOT retry internally in a way that
/// blocks the caller for longer than a short, bounded connect attempt.
/// </summary>
public interface IEventBusPublisher
{
    Task PublishAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken);
}
