using WaIngest.Application.Common.Interfaces;
using wavio.SharedDataModel.Entities.Ingest;
using wavio.SharedDataModel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace WaIngest.Infrastructure.Persistence;

/// <summary>
/// Adapts the shared <see cref="WavioDbContext"/> to <see cref="IWaIngestDbContext"/>, exposing
/// only the <c>ingest</c> schema entity sets this service owns. Same pattern as
/// core.Infrastructure's <c>CoreDbContext</c> — Application handlers depend on the interface, not
/// the shared concrete context.
/// </summary>
public sealed class WaIngestDbContext : IWaIngestDbContext
{
    private readonly WavioDbContext _db;

    public WaIngestDbContext(WavioDbContext db) => _db = db;

    public DbSet<RawWebhook> RawWebhooks => _db.RawWebhooks;
    public DbSet<WebhookDedupe> WebhookDedupes => _db.WebhookDedupes;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        _db.SaveChangesAsync(cancellationToken);
}
