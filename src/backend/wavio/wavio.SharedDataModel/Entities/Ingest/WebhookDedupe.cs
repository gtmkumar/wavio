namespace wavio.SharedDataModel.Entities.Ingest;

/// <summary>
/// At-least-once delivery guard (ingest.webhook_dedupe, issue #13). Meta redelivers webhooks on
/// timeout; the processor records one row per normalized sub-event it has SUCCESSFULLY published
/// so a redelivered copy (or a partially-failed retry of the same raw row) is recognized and does
/// not produce a second bus message.
///
/// <see cref="Wamid"/> is not always a literal WhatsApp message id — for event kinds that have no
/// natural message id (template/quality/tier/account events), the normalizer derives a stable
/// synthetic key (see WaIngest.Application's MetaWebhookNormalizer) and stores it here instead.
/// The column is a plain dedupe key, not an FK.
///
/// No raw_webhook_id back-reference on purpose: raw_webhooks partitions are dropped wholesale by
/// TTL maintenance, which would break any FK pointing at them (db/migrations/V003 header comment).
/// </summary>
public class WebhookDedupe
{
    public string Wamid { get; set; } = null!;
    public string EventType { get; set; } = null!;
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}
