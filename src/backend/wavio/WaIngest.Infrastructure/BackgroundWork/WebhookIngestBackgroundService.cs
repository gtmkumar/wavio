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
/// On startup, also recovers rows a previous instance persisted but never got to hand off/finish
/// (process killed between the DB insert and the enqueue, or mid-processing) — this is what makes
/// "ingest never drops" true across a restart, not just within one process's lifetime.
/// </summary>
public sealed partial class WebhookIngestBackgroundService : BackgroundService
{
    // A row created within this window of "now" at startup is treated as still in-flight from a
    // healthy request (its enqueue may simply not have happened yet), not as crash residue.
    private static readonly TimeSpan StartupStaleWindow = TimeSpan.FromSeconds(5);
    private const int StartupRecoveryBatchLimit = 1000;

    private readonly IWebhookIngestBuffer _buffer;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WebhookIngestBackgroundService> _logger;

    public WebhookIngestBackgroundService(
        IWebhookIngestBuffer buffer,
        IServiceScopeFactory scopeFactory,
        ILogger<WebhookIngestBackgroundService> logger)
    {
        _buffer = buffer;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverStaleReceivedRowsAsync(stoppingToken);

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

    private async Task RecoverStaleReceivedRowsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IWaIngestDbContext>();

        var staleCutoff = DateTimeOffset.UtcNow - StartupStaleWindow;

        var stale = await db.RawWebhooks.AsNoTracking()
            .Where(w => w.ProcessingStatus == "received" && w.ReceivedAt < staleCutoff)
            .OrderBy(w => w.ReceivedAt)
            .Select(w => new { w.Id, w.ReceivedAt })
            .Take(StartupRecoveryBatchLimit)
            .ToListAsync(cancellationToken);

        foreach (var row in stale)
            await _buffer.EnqueueAsync(new RawWebhookRef(row.Id, row.ReceivedAt), cancellationToken);

        if (stale.Count > 0)
            LogRecoveredStaleRows(_logger, stale.Count);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled error draining webhook ingest buffer for {Id}")]
    private static partial void LogUnhandledDrainError(ILogger logger, Exception exception, Guid id);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Recovered {Count} stale raw_webhooks row(s) left 'received' by a prior instance")]
    private static partial void LogRecoveredStaleRows(ILogger logger, int count);
}
