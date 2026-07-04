using WaIngest.Application.Common.Interfaces;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace WaIngest.Application.Ingestion.Commands.ReplayWebhooks;

public sealed class ReplayWebhooksHandler : ICommandHandler<ReplayWebhooksCommand, ReplayWebhooksResult>
{
    private const int DefaultMaxCount = 500;
    private const int HardCap = 5000;

    private readonly IWaIngestDbContext _db;
    private readonly IWebhookProcessor _processor;

    public ReplayWebhooksHandler(IWaIngestDbContext db, IWebhookProcessor processor)
    {
        _db = db;
        _processor = processor;
    }

    public async Task<ReplayWebhooksResult> HandleAsync(ReplayWebhooksCommand command, CancellationToken cancellationToken)
    {
        var maxCount = Math.Clamp(command.MaxCount <= 0 ? DefaultMaxCount : command.MaxCount, 1, HardCap);

        // SignatureValid == true is non-negotiable, even for an explicit Id: a delivery that
        // failed signature verification must never be replayable into a real bus event — the
        // whole point of persisting it was forensics, not "process it later" (WebhookProcessor
        // enforces this same rule independently as a second layer).
        var query = _db.RawWebhooks.AsNoTracking()
            .Where(w => w.SignatureValid == true);

        query = command.Id is { } id
            ? query.Where(w => w.Id == id)
            // Default scope: only rows that never finished successfully. A caller who passes an
            // explicit Id is asking to force-replay that exact row regardless of its status.
            : query.Where(w => w.ProcessingStatus == "received" || w.ProcessingStatus == "failed");

        if (command.Since is { } since) query = query.Where(w => w.ReceivedAt >= since);
        if (command.Until is { } until) query = query.Where(w => w.ReceivedAt <= until);

        var targets = await query
            .OrderBy(w => w.ReceivedAt)
            .Select(w => new { w.Id, w.ReceivedAt })
            .Take(maxCount)
            .ToListAsync(cancellationToken);

        foreach (var target in targets)
            await _processor.ProcessAsync(target.Id, target.ReceivedAt, cancellationToken);

        var targetIds = targets.Select(t => t.Id).ToList();
        var stillFailed = targetIds.Count == 0
            ? 0
            : await _db.RawWebhooks.AsNoTracking()
                .CountAsync(w => targetIds.Contains(w.Id) && w.ProcessingStatus == "failed", cancellationToken);

        return new ReplayWebhooksResult(targets.Count, targets.Count, stillFailed);
    }
}
