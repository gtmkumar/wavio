using wavio.SharedDataModel.Entities.Ingest;
using Microsoft.EntityFrameworkCore;

namespace WaIngest.Application.Common.Interfaces;

/// <summary>
/// The ingest context's data-access surface, exposed to Application handlers as an interface (no
/// repositories — same convention as core.Application's <c>ICoreDbContext</c>). Backed by the
/// shared <c>WavioDbContext</c> via an adapter in WaIngest.Infrastructure. Only the entity sets
/// wa-ingest-svc touches are surfaced here: <c>ingest.raw_webhooks</c> and
/// <c>ingest.webhook_dedupe</c> — both schema-owned by this service, not tenant-scoped (spec §5,
/// db/migrations/V003 header comment).
/// </summary>
public interface IWaIngestDbContext
{
    DbSet<RawWebhook> RawWebhooks { get; }
    DbSet<WebhookDedupe> WebhookDedupes { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
