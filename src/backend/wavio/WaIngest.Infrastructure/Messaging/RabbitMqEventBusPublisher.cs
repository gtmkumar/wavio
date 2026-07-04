using System.Text.Json;
using WaIngest.Application.Common.Interfaces;
using WaPlatform.Contracts.IntegrationEvents;
using RabbitMQ.Client;

namespace WaIngest.Infrastructure.Messaging;

/// <summary>
/// Publishes integration events to RabbitMQ (spec §7.2). Exchange <c>wavio.events</c> — durable
/// topic exchange, routing key = <see cref="IntegrationEvent.EventName"/> (e.g.
/// <c>wa.message.received.v1</c>). Consumers (Wave 1+: #14/#15/#16, Wave 2+: #19/#20/#21) bind
/// their own durable queues to this exchange with a routing pattern matching the events they
/// care about (e.g. <c>wa.message.status.#</c>).
///
/// Declares the exchange idempotently on first use rather than requiring a separate topology
/// migration step — safe because <c>ExchangeDeclareAsync</c> is a no-op when the exchange already
/// exists with the same arguments.
/// </summary>
public sealed class RabbitMqEventBusPublisher : IEventBusPublisher
{
    public const string ExchangeName = "wavio.events";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RabbitMqConnectionManager _connectionManager;

    public RabbitMqEventBusPublisher(RabbitMqConnectionManager connectionManager) =>
        _connectionManager = connectionManager;

    public async Task PublishAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        // No PII in the routing key or message properties — wa_id (when present) only ever
        // travels inside the serialized body, never in a header/property a broker admin UI would
        // surface unmasked in a list view. The body itself is not logged anywhere in this path.
        await using var channel = await _connectionManager.CreateChannelAsync(cancellationToken);

        await channel.ExchangeDeclareAsync(
            exchange: ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

        var body = JsonSerializer.SerializeToUtf8Bytes(
            integrationEvent, integrationEvent.GetType(), JsonOptions);

        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            MessageId = integrationEvent.EventId.ToString(),
            Type = integrationEvent.EventName,
            Timestamp = new AmqpTimestamp(integrationEvent.OccurredAt.ToUnixTimeSeconds())
        };

        await channel.BasicPublishAsync(
            exchange: ExchangeName,
            routingKey: integrationEvent.EventName,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);
    }
}
