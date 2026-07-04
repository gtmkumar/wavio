namespace WaIngest.Application.Ingestion;

/// <summary>
/// Dedupes, normalizes, and publishes one already-persisted <c>ingest.raw_webhooks</c> row. Used
/// both by the live background worker (draining <see cref="IWebhookIngestBuffer"/>) and by the
/// replay command — reprocessing is always safe/idempotent (see <see cref="WebhookProcessor"/>
/// remarks on dedupe ordering).
/// </summary>
public interface IWebhookProcessor
{
    Task ProcessAsync(Guid id, DateTimeOffset receivedAt, CancellationToken cancellationToken);
}
