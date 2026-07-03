namespace WaIngest.Application.Ingestion.Dtos;

/// <summary>Identifies one <c>ingest.raw_webhooks</c> row. The composite PK (Id, ReceivedAt) is
/// required by PG range partitioning (db/migrations/V003), so both parts travel together
/// end-to-end: from the persist command, through the in-memory processing queue, to replay.</summary>
public sealed record RawWebhookRef(Guid Id, DateTimeOffset ReceivedAt);
