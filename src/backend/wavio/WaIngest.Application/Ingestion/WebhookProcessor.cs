using System.Text.Json;
using WaIngest.Application.Common.Interfaces;
using WaIngest.Application.Ingestion.Normalization;
using wavio.SharedDataModel.Entities.Ingest;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace WaIngest.Application.Ingestion;

/// <summary>
/// Dedupe → normalize → publish for one raw webhook row (spec §4.3). Runs entirely off the HTTP
/// request path (background worker or replay command) — nothing here is on the ack latency
/// budget.
///
/// Dedupe/publish ordering (why replay after a RabbitMQ outage is safe, and why a genuine Meta
/// redelivery is not double-published):
///   1. For each normalized sub-event, SELECT-check <c>ingest.webhook_dedupe</c> for
///      (DedupeKey, DedupeEventType). If found, this exact change was already published
///      successfully (by an earlier delivery of the same webhook, or an earlier attempt at this
///      same row) — skip it and move on.
///   2. Otherwise, publish. Only once the publish call returns successfully do we INSERT the
///      dedupe row.
///   3. If publish throws (RabbitMQ down/unreachable — degraded mode, spec §8), we do NOT insert a
///      dedupe row and we do NOT rethrow past this row — the row is marked 'failed' and the
///      sub-event is left exactly as if it had never been attempted. The replay tool re-runs this
///      same process later: the SELECT-check still finds nothing, so it publishes for the first
///      time. This is why dedupe-insert must happen AFTER publish succeeds, never before.
///
/// A single background worker (no parallel consumers) processes the queue sequentially, so the
/// SELECT-then-INSERT has no concurrent-writer race in this deployment shape; if that ever changes
/// (multiple worker instances), <c>ingest.webhook_dedupe</c>'s PRIMARY KEY still turns any
/// concurrent double-publish into a harmless unique-violation on the dedupe insert (accepted
/// as at-least-once delivery — contract rule: consumers must be idempotent on EventId).
/// </summary>
public sealed partial class WebhookProcessor : IWebhookProcessor
{
    private readonly IWaIngestDbContext _db;
    private readonly IEventBusPublisher _bus;
    private readonly ILogger<WebhookProcessor> _logger;

    public WebhookProcessor(IWaIngestDbContext db, IEventBusPublisher bus, ILogger<WebhookProcessor> logger)
    {
        _db = db;
        _bus = bus;
        _logger = logger;
    }

    public async Task ProcessAsync(Guid id, DateTimeOffset receivedAt, CancellationToken cancellationToken)
    {
        var raw = await _db.RawWebhooks
            .FirstOrDefaultAsync(w => w.Id == id && w.ReceivedAt == receivedAt, cancellationToken);

        if (raw is null)
        {
            LogRowNotFound(_logger, id, receivedAt);
            return;
        }

        // Reprocessing an already-terminal row (replay re-scanning a row another replay call
        // already finished) is harmless — dedupe checks make it a no-op — but skip the work.
        if (raw.ProcessingStatus is "processed" or "skipped")
            return;

        // Defense in depth: a row with an invalid/missing signature must NEVER be normalized or
        // published, through any entry point — the endpoint already never enqueues these, but a
        // future/careless caller of ProcessAsync (replay, a fix-up script, ...) must not be able
        // to "launder" an unsigned/forged delivery into a real bus event just by naming its id.
        // ReplayWebhooksHandler also filters these out at the query level; this is the second,
        // independent layer.
        if (raw.SignatureValid != true)
        {
            raw.ProcessingStatus = "skipped";
            raw.ProcessingError = "refusing to process: signature was not valid at receipt";
            raw.ProcessedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        try
        {
            await ProcessCoreAsync(raw, cancellationToken);
        }
        catch (Exception ex)
        {
            // Never let an unexpected fragment/DB hiccup take down the worker loop — this row
            // stays (or becomes) 'failed' and is picked up again by the replay tool.
            LogUnexpectedFailure(_logger, ex, id, receivedAt);
            raw.ProcessingStatus = "failed";
            raw.ProcessingError = $"unexpected error: {ex.GetType().Name}";
            raw.ProcessedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task ProcessCoreAsync(RawWebhook raw, CancellationToken cancellationToken)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(raw.Payload);
        }
        catch (JsonException ex)
        {
            raw.ProcessingStatus = "failed";
            raw.ProcessingError = $"invalid JSON payload: {ex.Message}";
            raw.ProcessedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        using (document)
        {
            var normalized = MetaWebhookNormalizer.Normalize(document.RootElement, out var skipped);

            if (normalized.Count == 0)
            {
                raw.ProcessingStatus = "skipped";
                raw.ProcessingError = skipped.Count > 0
                    ? Truncate(string.Join("; ", skipped))
                    : "no recognizable event in payload";
                raw.ProcessedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
                return;
            }

            var publishFailures = new List<string>();

            foreach (var item in normalized)
            {
                var alreadyPublished = await _db.WebhookDedupes.AsNoTracking()
                    .AnyAsync(d => d.Wamid == item.DedupeKey && d.EventType == item.DedupeEventType, cancellationToken);

                if (alreadyPublished)
                    continue;

                using var scope = _logger.BeginScope(new Dictionary<string, object>
                {
                    ["Wamid"] = item.DedupeKey,
                    ["EventName"] = item.Event.EventName
                });

                try
                {
                    await _bus.PublishAsync(item.Event, cancellationToken);
                }
                catch (Exception ex)
                {
                    LogPublishFailed(_logger, ex, item.Event.EventName, item.DedupeKey);
                    publishFailures.Add(item.Event.EventName);
                    continue; // do NOT record dedupe — see class remarks.
                }

                _db.WebhookDedupes.Add(new WebhookDedupe
                {
                    Wamid = item.DedupeKey,
                    EventType = item.DedupeEventType,
                    FirstSeenAt = DateTimeOffset.UtcNow,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }

            raw.ProcessingStatus = publishFailures.Count == 0 ? "processed" : "failed";
            raw.ProcessingError = publishFailures.Count == 0
                ? (skipped.Count > 0 ? Truncate("partially recognized; skipped: " + string.Join("; ", skipped)) : null)
                : Truncate("bus publish failed for: " + string.Join(", ", publishFailures));
            raw.ProcessedAt = DateTimeOffset.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    // processing_error is a plain text column with no explicit length cap in the DB, but keep
    // it bounded defensively — this column must never carry the payload itself (PII), only
    // short diagnostic text.
    private static string Truncate(string value) => value.Length <= 2000 ? value : value[..2000];

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "raw_webhooks row {Id}/{ReceivedAt:O} not found — skipping (already TTL-dropped?)")]
    private static partial void LogRowNotFound(ILogger logger, Guid id, DateTimeOffset receivedAt);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Unexpected failure processing raw_webhooks {Id}/{ReceivedAt:O}")]
    private static partial void LogUnexpectedFailure(ILogger logger, Exception exception, Guid id, DateTimeOffset receivedAt);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Publish failed for {EventName} (dedupe key {DedupeKey}) — bus degraded, deferring to replay")]
    private static partial void LogPublishFailed(ILogger logger, Exception exception, string eventName, string dedupeKey);
}
