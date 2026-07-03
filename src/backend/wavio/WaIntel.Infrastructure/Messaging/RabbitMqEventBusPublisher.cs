using System.Text.Json;
using WaIntel.Application.Common.Interfaces;
using WaPlatform.Contracts.IntegrationEvents;
using RabbitMQ.Client;

namespace WaIntel.Infrastructure.Messaging;

/// <summary>
/// Publishes integration events to RabbitMQ (spec §7.2) — exchange <c>wavio.events</c>, durable
/// topic, routing key = event name (e.g. <c>wa.window.closing.v1</c>). Same exchange
/// WaIngest.Infrastructure's publisher declares; declaring it again here idempotently (same
/// name/type/durability) is a no-op if it already exists.
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
