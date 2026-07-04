using WaIngest.Application.Ingestion;
using WaIngest.Application.Ingestion.Dtos;
using Xunit;

namespace WaIngest.Tests.Ingestion;

public class WebhookIngestBufferTests
{
    [Fact]
    public async Task EnqueueAsync_WhenFull_DropsNewestSilentlyWithoutBlockingOrThrowing()
    {
        // Regression test (security review, S4): under BoundedChannelFullMode.Wait, sustained
        // traffic during a bus outage would fill the buffer and EnqueueAsync would then BLOCK the
        // webhook ack path — breaking the <1s NFR and amplifying Meta's own retry behavior. DropWrite
        // must never block, and the item that doesn't fit must be silently discarded (the row is
        // already durable in Postgres; the periodic sweep recovers it).
        var buffer = new WebhookIngestBuffer(capacity: 2);
        var a = new RawWebhookRef(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var b = new RawWebhookRef(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var c = new RawWebhookRef(Guid.NewGuid(), DateTimeOffset.UtcNow); // must be dropped — buffer full

        await buffer.EnqueueAsync(a, CancellationToken.None);
        await buffer.EnqueueAsync(b, CancellationToken.None);

        var enqueueThirdTask = buffer.EnqueueAsync(c, CancellationToken.None).AsTask();
        var winner = await Task.WhenAny(enqueueThirdTask, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(enqueueThirdTask, winner); // must never block waiting for space

        using var cts = new CancellationTokenSource();
        var drained = new List<RawWebhookRef>();
        await foreach (var item in buffer.ReadAllAsync(cts.Token))
        {
            drained.Add(item);
            if (drained.Count == 2)
            {
                cts.Cancel();
                break;
            }
        }

        Assert.Equal([a, b], drained);
    }

    [Fact]
    public async Task EnqueueAsync_WhenNotFull_DeliversItemNormally()
    {
        var buffer = new WebhookIngestBuffer(capacity: 10);
        var item = new RawWebhookRef(Guid.NewGuid(), DateTimeOffset.UtcNow);

        await buffer.EnqueueAsync(item, CancellationToken.None);

        using var cts = new CancellationTokenSource();
        RawWebhookRef? received = null;
        await foreach (var i in buffer.ReadAllAsync(cts.Token))
        {
            received = i;
            cts.Cancel();
            break;
        }

        Assert.Equal(item, received);
    }
}
