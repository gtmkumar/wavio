using System.Threading.Channels;
using WaIngest.Application.Ingestion.Dtos;

namespace WaIngest.Application.Ingestion;

/// <summary>
/// Bounded in-process channel backing <see cref="IWebhookIngestBuffer"/>. Singleton — one buffer
/// per host instance, drained by a single background worker (WaIngest.Infrastructure). Bounded
/// with <see cref="BoundedChannelFullMode.Wait"/> so a slow/stalled worker applies backpressure on
/// enqueue rather than silently dropping a webhook reference (the row itself is already durable in
/// Postgres either way — this only governs how fast the in-memory hand-off proceeds).
/// </summary>
public sealed class WebhookIngestBuffer : IWebhookIngestBuffer
{
    private readonly Channel<RawWebhookRef> _channel = Channel.CreateBounded<RawWebhookRef>(
        new BoundedChannelOptions(capacity: 10_000)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });

    public ValueTask EnqueueAsync(RawWebhookRef item, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(item, cancellationToken);

    public IAsyncEnumerable<RawWebhookRef> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
