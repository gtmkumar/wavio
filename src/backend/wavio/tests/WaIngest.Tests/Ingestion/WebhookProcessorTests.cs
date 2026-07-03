using WaIngest.Application.Common.Interfaces;
using WaIngest.Application.Ingestion;
using wavio.SharedDataModel.Entities.Ingest;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace WaIngest.Tests.Ingestion;

public class WebhookProcessorTests
{
    private const string StatusPayload = """
    {
      "entry": [{
        "id": "waba-123",
        "changes": [{
          "field": "messages",
          "value": {
            "metadata": { "phone_number_id": "phone-1" },
            "statuses": [{ "id": "wamid.DUPTEST", "status": "sent", "timestamp": "1700000000" }]
          }
        }]
      }]
    }
    """;

    private const string TemplateStatusPayload = """
    {
      "entry": [{
        "id": "waba-123",
        "changes": [{
          "field": "message_template_status_update",
          "value": { "message_template_id": "tpl-1", "event": "PAUSED", "reason": "quality" }
        }]
      }]
    }
    """;

    private static RawWebhook NewRawWebhook(string payload)
    {
        var now = DateTimeOffset.UtcNow;
        return new RawWebhook
        {
            Id = Guid.NewGuid(),
            ReceivedAt = now,
            Source = "meta",
            SignatureValid = true,
            Payload = payload,
            ProcessingStatus = "received",
            CreatedAt = now
        };
    }

    [Fact]
    public async Task ProcessAsync_DuplicateWebhook_PublishesOnlyOnce()
    {
        await using var db = InMemoryWaIngestDbContext.Create(nameof(ProcessAsync_DuplicateWebhook_PublishesOnlyOnce));

        // Two separate raw deliveries carrying the identical status update — simulates Meta
        // redelivering the same webhook (spec §4.3: "Meta retries webhooks").
        var first = NewRawWebhook(StatusPayload);
        var second = NewRawWebhook(StatusPayload);
        db.RawWebhooks.AddRange(first, second);
        await db.SaveChangesAsync(CancellationToken.None);

        var bus = new Mock<IEventBusPublisher>();
        bus.Setup(b => b.PublishAsync(It.IsAny<WaPlatform.Contracts.IntegrationEvents.IntegrationEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var processor = new WebhookProcessor(db, bus.Object, NullLogger<WebhookProcessor>.Instance);

        await processor.ProcessAsync(first.Id, first.ReceivedAt, CancellationToken.None);
        await processor.ProcessAsync(second.Id, second.ReceivedAt, CancellationToken.None);

        bus.Verify(b => b.PublishAsync(It.IsAny<WaPlatform.Contracts.IntegrationEvents.IntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(1, db.WebhookDedupes.Count());
        Assert.Equal("processed", first.ProcessingStatus);
        Assert.Equal("processed", second.ProcessingStatus);
    }

    [Fact]
    public async Task ProcessAsync_BusDown_MarksRowFailedAndDoesNotRecordDedupe()
    {
        await using var db = InMemoryWaIngestDbContext.Create(nameof(ProcessAsync_BusDown_MarksRowFailedAndDoesNotRecordDedupe));

        var raw = NewRawWebhook(TemplateStatusPayload);
        db.RawWebhooks.Add(raw);
        await db.SaveChangesAsync(CancellationToken.None);

        var bus = new Mock<IEventBusPublisher>();
        bus.Setup(b => b.PublishAsync(It.IsAny<WaPlatform.Contracts.IntegrationEvents.IntegrationEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("RabbitMQ unreachable"));

        var processor = new WebhookProcessor(db, bus.Object, NullLogger<WebhookProcessor>.Instance);

        await processor.ProcessAsync(raw.Id, raw.ReceivedAt, CancellationToken.None);

        Assert.Equal("failed", raw.ProcessingStatus);
        Assert.NotNull(raw.ProcessingError);
        Assert.Empty(db.WebhookDedupes);
    }

    [Fact]
    public async Task ProcessAsync_ReplayAfterBusRecovers_PublishesAndMarksProcessed()
    {
        await using var db = InMemoryWaIngestDbContext.Create(nameof(ProcessAsync_ReplayAfterBusRecovers_PublishesAndMarksProcessed));

        var raw = NewRawWebhook(TemplateStatusPayload);
        db.RawWebhooks.Add(raw);
        await db.SaveChangesAsync(CancellationToken.None);

        // First attempt: bus is down (degraded mode).
        var failingBus = new Mock<IEventBusPublisher>();
        failingBus.Setup(b => b.PublishAsync(It.IsAny<WaPlatform.Contracts.IntegrationEvents.IntegrationEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("RabbitMQ unreachable"));

        var processor1 = new WebhookProcessor(db, failingBus.Object, NullLogger<WebhookProcessor>.Instance);
        await processor1.ProcessAsync(raw.Id, raw.ReceivedAt, CancellationToken.None);

        Assert.Equal("failed", raw.ProcessingStatus);
        Assert.Empty(db.WebhookDedupes);

        // Replay: bus is back — this is exactly what ReplayWebhooksHandler does, re-invoking
        // IWebhookProcessor.ProcessAsync for rows still 'failed'.
        var recoveredBus = new Mock<IEventBusPublisher>();
        recoveredBus.Setup(b => b.PublishAsync(It.IsAny<WaPlatform.Contracts.IntegrationEvents.IntegrationEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var processor2 = new WebhookProcessor(db, recoveredBus.Object, NullLogger<WebhookProcessor>.Instance);
        await processor2.ProcessAsync(raw.Id, raw.ReceivedAt, CancellationToken.None);

        recoveredBus.Verify(b => b.PublishAsync(It.IsAny<WaPlatform.Contracts.IntegrationEvents.IntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("processed", raw.ProcessingStatus);
        Assert.Equal(1, db.WebhookDedupes.Count());
    }

    [Fact]
    public async Task ProcessAsync_MissingRow_DoesNotThrow()
    {
        await using var db = InMemoryWaIngestDbContext.Create(nameof(ProcessAsync_MissingRow_DoesNotThrow));

        var bus = new Mock<IEventBusPublisher>();
        var processor = new WebhookProcessor(db, bus.Object, NullLogger<WebhookProcessor>.Instance);

        // Should log and return, never throw — e.g. a TTL-dropped partition raced with a replay.
        var exception = await Record.ExceptionAsync(() =>
            processor.ProcessAsync(Guid.NewGuid(), DateTimeOffset.UtcNow, CancellationToken.None));

        Assert.Null(exception);
        bus.Verify(b => b.PublishAsync(It.IsAny<WaPlatform.Contracts.IntegrationEvents.IntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
