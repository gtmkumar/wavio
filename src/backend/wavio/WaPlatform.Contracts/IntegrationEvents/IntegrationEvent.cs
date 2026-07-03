namespace WaPlatform.Contracts.IntegrationEvents;

/// <summary>
/// Base envelope for every integration event published on RabbitMQ (spec §7.2).
///
/// Contract rules:
///   • Additive-only within a major version — never remove/rename/retype a property of a
///     published Vn event; breaking changes ship as a new Vn+1 record published alongside.
///   • <see cref="EventName"/> is the RabbitMQ routing key (e.g. <c>wa.message.received.v1</c>).
///   • Consumers must be idempotent on <see cref="EventId"/> (at-least-once delivery).
///
/// PII note (spec §5): <c>wa_id</c> is personal data — events carry it for consumers that
/// need it, but it must never be logged unmasked (use <c>PiiMask</c>) and is excluded from
/// OTel trace attributes.
/// </summary>
public abstract record IntegrationEvent
{
    /// <summary>Unique id of this event instance; consumers dedupe on it.</summary>
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <summary>UTC instant the source service recorded the event.</summary>
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Platform tenant the event belongs to (spec §5 tenant model).</summary>
    public Guid TenantId { get; init; }

    /// <summary>RabbitMQ routing key, versioned (e.g. <c>wa.message.received.v1</c>).</summary>
    public abstract string EventName { get; }
}
