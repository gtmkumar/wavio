namespace wavio.SharedDataModel.Entities.Kernel;

/// <summary>
/// Per-consumer "inbox" marker recording which <see cref="OutboxEvent"/> rows a named worker
/// consumer has durably processed (kernel.outbox_consumed_events).
///
/// Unlike the time-watermark cursor (engagement_cms.notification_event_cursors), this table records
/// EACH consumed event id, so a consumer can query the set of UNPROCESSED events with a simple
/// anti-join regardless of <c>occurred_at</c> ordering. That makes delivery NO-SKIP: an event that
/// commits out of OccurredAt order, or shares its OccurredAt with a whole batch of siblings, can
/// never be stepped over by a moving watermark.
///
/// Introduced for consumer_name = 'partner_booking_debit' — a money path where a skipped debit is
/// direct revenue loss (a booking is created but the partner wallet is never charged). Keyed
/// generically by (consumer_name, event_id) so other outbox consumers (e.g. loyalty_earn) can adopt
/// the same no-skip pattern later without further schema churn.
/// </summary>
public class OutboxConsumedEvent
{
    /// <summary>Logical name of the consuming service (e.g. 'partner_booking_debit'). Part of the PK.</summary>
    public string ConsumerName { get; set; } = null!;

    /// <summary>Id of the kernel.outbox_events row this consumer has finished with. Part of the PK.</summary>
    public Guid EventId { get; set; }

    /// <summary>When the consumer marked the event processed (audit/retention only).</summary>
    public DateTimeOffset ProcessedAt { get; set; }
}
