using WaIngest.Application.Common.Interfaces;
using WaIngest.Application.Ingestion;
using WaIngest.Application.Ingestion.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WaIngest.Infrastructure.BackgroundWork;

/// <summary>
/// Drains <see cref="IWebhookIngestBuffer"/> and runs <see cref="IWebhookProcessor"/> for each raw
/// webhook — the "all processing async" half of spec §4.3. Single reader by design (the buffer
/// hand-off + dedupe-before-publish ordering assumes sequential processing, see
/// <c>WebhookProcessor</c> remarks).
///
/// Runs two concurrent loops for the lifetime of the host:
///   1. <see cref="DrainBufferAsync"/> — processes whatever the endpoint successfully enqueued.
///   2. <see cref="PeriodicSweepAsync"/> — every <see cref="_sweepInterval"/>, re-scans for
///      'received' rows older than <see cref="StaleWindow"/> and re-enqueues them. This is
///      deliberately NOT a startup-only scan: since <c>WebhookIngestBuffer</c> uses
///      <c>DropWrite</c> (spec §8 — the ack path must never block on a full buffer), a row can
///      be durably persisted but never make it into the in-memory buffer at all (dropped because
///      the buffer was full, not just because a prior instance crashed). The periodic sweep is
///      what recovers those without requiring a manual replay call.
/// </summary>
public sealed partial class WebhookIngestBackgroundService : BackgroundService
{
    // A row created within this window of "now" is treated as still legitimately in-flight
    // (its enqueue may simply not have happened yet, or a worker may already be processing it),
    // not as abandoned/dropped.
    private static readonly TimeSpan StaleWindow = TimeSpan.FromSeconds(5);
    private const int SweepBatchLimit = 1000;
    private static readonly TimeSpan DefaultSweepInterval = TimeSpan.FromSeconds(30);

    private readonly IWebhookIngestBuffer _buffer;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WebhookIngestBackgroundService> _logger;
    private readonly TimeSpan _sweepInterval;

    public WebhookIngestBackgroundService(
        IWebhookIngestBuffer buffer,
        IServiceScopeFactory scopeFactory,
        ILogger<WebhookIngestBackgroundService> logger,
        TimeSpan? sweepInterval = null)
    {
        _buffer = buffer;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _sweepInterval = sweepInterval ?? DefaultSweepInterval;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(
            DrainBufferAsync(stoppingToken),
            PeriodicSweepAsync(stoppingToken));

    private async Task DrainBufferAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in _buffer.ReadAllAsync(stoppingToken))
        {
            using var scope = _scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<IWebhookProcessor>();

            try
            {
                await processor.ProcessAsync(item.Id, item.ReceivedAt, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // WebhookProcessor already catches and records its own failures; this is a
                // last-resort net so one pathological item can never kill the worker loop.
                LogUnhandledDrainError(_logger, ex, item.Id);
            }
        }
    }

    private async Task PeriodicSweepAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_sweepInterval);

        // Run once immediately on startup (covers the crash-between-insert-and-enqueue case),
        // then on every subsequent tick (covers buffer-full drops at runtime — see class remarks).
        do
        {
            try
            {
                await SweepStaleReceivedRowsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogSweepFailed(_logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SweepStaleReceivedRowsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IWaIngestDbContext>();

        var staleCutoff = DateTimeOffset.UtcNow - StaleWindow;

        var stale = await db.RawWebhooks.AsNoTracking()
            .Where(w => w.ProcessingStatus == "received" && w.ReceivedAt < staleCutoff)
            .OrderBy(w => w.ReceivedAt)
            .Select(w => new { w.Id, w.ReceivedAt })
            .Take(SweepBatchLimit)
            .ToListAsync(cancellationToken);

        foreach (var row in stale)
            await _buffer.EnqueueAsync(new RawWebhookRef(row.Id, row.ReceivedAt), cancellationToken);

        if (stale.Count > 0)
            LogRecoveredStaleRows(_logger, stale.Count);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled error draining webhook ingest buffer for {Id}")]
    private static partial void LogUnhandledDrainError(ILogger logger, Exception exception, Guid id);

    [LoggerMessage(Level = LogLevel.Error, Message = "Periodic stale-row sweep failed — will retry next interval")]
    private static partial void LogSweepFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Recovered {Count} stale raw_webhooks row(s) left 'received' (prior crash or a dropped buffer enqueue)")]
    private static partial void LogRecoveredStaleRows(ILogger logger, int count);
}
