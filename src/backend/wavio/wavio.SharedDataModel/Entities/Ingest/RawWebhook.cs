namespace wavio.SharedDataModel.Entities.Ingest;

/// <summary>
/// Verbatim Meta webhook delivery (ingest.raw_webhooks, issue #13). Written by the receiver
/// BEFORE the payload is parsed — durability comes first, ack comes second, normalization
/// happens later on a background worker. Partitioned weekly on <see cref="ReceivedAt"/> with a
/// 30-day TTL (db/migrations/V003__ingest.sql); composite PK (Id, ReceivedAt) is required by PG
/// range partitioning, same pattern as <c>AuditLog</c>.
///
/// Deliberately NOT tenant-scoped (no RLS, db/migrations/V003 header comment): rows are written
/// before tenant resolution is possible. <see cref="TenantId"/> is nullable and only ever
/// backfilled once a resolution path exists (not built in Wave 1 — see WaIngest.Application's
/// webhook normalizer notes).
/// </summary>
public class RawWebhook
{
    public Guid Id { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }

    /// <summary>Always "meta" today; column exists for future channel additions.</summary>
    public string Source { get; set; } = "meta";

    public Guid? TenantId { get; set; }

    /// <summary>Null when the request carried no signature header at all (still persisted for
    /// forensics before being rejected with 401 — see WebhookSignatureVerifier).</summary>
    public bool? SignatureValid { get; set; }

    /// <summary>Raw request headers as a JSON object (jsonb) — Authorization/secret headers are
    /// never copied in here (see the endpoint's header allow-list).</summary>
    public string? Headers { get; set; }

    /// <summary>Raw JSON body exactly as received (jsonb), captured before any parsing.</summary>
    public string Payload { get; set; } = null!;

    /// <summary>received | processed | failed | skipped (CHECK-enforced in the DB).</summary>
    public string ProcessingStatus { get; set; } = "received";

    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>Non-PII diagnostic text only (never the payload, never a secret) — see
    /// WebhookProcessor's exception handling.</summary>
    public string? ProcessingError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}
