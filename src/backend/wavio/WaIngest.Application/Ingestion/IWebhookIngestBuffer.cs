using WaIngest.Application.Ingestion.Dtos;

namespace WaIngest.Application.Ingestion;

/// <summary>
/// In-process hand-off from the webhook endpoint (which only persists raw + acks) to the
/// background worker that does the actual dedupe/normalize/publish (spec §4.3: "all processing
/// async" — normalization must never block the &lt;500ms ack). Enqueueing is fire-and-forget from
/// the request's point of view; durability comes from the row already being committed to
/// <c>ingest.raw_webhooks</c> before this is called; a dropped/skipped enqueue (e.g. process
/// restart) is recovered by the worker's startup catch-up scan and by the replay tool — never by
/// this buffer alone.
/// </summary>
public interface IWebhookIngestBuffer
{
    ValueTask EnqueueAsync(RawWebhookRef item, CancellationToken cancellationToken);

    IAsyncEnumerable<RawWebhookRef> ReadAllAsync(CancellationToken cancellationToken);
}
