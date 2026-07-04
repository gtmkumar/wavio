using System.Threading.Channels;
using WaIngest.Application.Ingestion.Dtos;

namespace WaIngest.Application.Ingestion;

/// <summary>
/// Bounded in-process channel backing <see cref="IWebhookIngestBuffer"/>. Singleton — one buffer
/// per host instance, drained by a single background worker (WaIngest.Infrastructure).
///
/// Uses <see cref="BoundedChannelFullMode.DropWrite"/>, not <c>Wait</c>: the row referenced by an
/// enqueue is already durable in <c>ingest.raw_webhooks</c> before this is ever called, so the
/// in-memory reference itself is disposable. If enqueue blocked instead (as <c>Wait</c> would)
/// during a sustained RabbitMQ outage — every webhook still gets persisted and dequeued into this
/// buffer, but the background worker can't drain it as fast as it fills — the HTTP ack path would
/// stall behind a full buffer, breaking the &lt;1s ack NFR and very likely triggering Meta's own
/// webhook retry amplification on top of an already-degraded bus. A dropped reference here is not
/// a dropped webhook: <c>WebhookIngestBackgroundService</c>'s periodic sweep (not just a
/// startup-only scan) picks up any 'received' row that fell out of this buffer.
/// </summary>
public sealed class WebhookIngestBuffer : IWebhookIngestBuffer
{
    private readonly Channel<RawWebhookRef> _channel;

    public WebhookIngestBuffer(int capacity = 10_000)
    {
        _channel = Channel.CreateBounded<RawWebhookRef>(
            new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropWrite
            });
    }

    public ValueTask EnqueueAsync(RawWebhookRef item, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // TryWrite never blocks (DropWrite mode): it either enqueues immediately or silently
        // discards the new item when full — see the class remarks for why dropping is correct.
        _channel.Writer.TryWrite(item);
        return ValueTask.CompletedTask;
    }

    public IAsyncEnumerable<RawWebhookRef> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
