using Wavio.Utilities.CQRS.Abstractions;

namespace WaIngest.Application.Ingestion.Commands.ReplayWebhooks;

/// <summary>
/// Re-runs dedupe/normalize/publish for already-persisted <c>ingest.raw_webhooks</c> rows — the
/// degraded-mode recovery path (spec §8: "ingest never drops") for whatever a RabbitMQ outage left
/// in <c>processing_status = 'failed'</c>, plus any 'received' rows a crashed worker never picked
/// up. Safe to call repeatedly: <see cref="WebhookProcessor"/>'s dedupe-after-publish ordering
/// means a row that already published successfully is a no-op the second time.
///
/// Scope is either a single row (<see cref="Id"/>) or a time window
/// (<see cref="Since"/>/<see cref="Until"/>, both optional, either/both bounds) — "id range" in
/// the issue is interpreted as "target one specific delivery by id" since raw_webhooks ids are
/// random UUIDs with no meaningful ordering to range over; the time window is the practical
/// "recover everything since the outage started" tool.
/// </summary>
public sealed record ReplayWebhooksCommand(
    Guid? Id,
    DateTimeOffset? Since,
    DateTimeOffset? Until,
    int MaxCount = 500) : ICommand<ReplayWebhooksResult>;

public sealed record ReplayWebhooksResult(int Scanned, int Reprocessed, int StillFailed);
