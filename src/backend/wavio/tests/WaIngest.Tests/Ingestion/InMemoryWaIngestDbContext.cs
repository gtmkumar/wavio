using WaIngest.Application.Common.Interfaces;
using wavio.SharedDataModel.Entities.Ingest;
using Microsoft.EntityFrameworkCore;

namespace WaIngest.Tests.Ingestion;

/// <summary>
/// Minimal <see cref="IWaIngestDbContext"/> stand-in for unit tests. <c>DbSet&lt;T&gt;</c> cannot
/// be hand-faked, so this uses the EF Core in-memory provider with just enough model
/// configuration (composite keys) to satisfy it — no relational annotations (jsonb, RLS, column
/// names) since the in-memory provider ignores them anyway.
/// </summary>
public sealed class InMemoryWaIngestDbContext : DbContext, IWaIngestDbContext
{
    public InMemoryWaIngestDbContext(DbContextOptions<InMemoryWaIngestDbContext> options) : base(options) { }

    public DbSet<RawWebhook> RawWebhooks => Set<RawWebhook>();
    public DbSet<WebhookDedupe> WebhookDedupes => Set<WebhookDedupe>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RawWebhook>().HasKey(e => new { e.Id, e.ReceivedAt });
        modelBuilder.Entity<WebhookDedupe>().HasKey(e => new { e.Wamid, e.EventType });
    }

    public static InMemoryWaIngestDbContext Create(string databaseName) =>
        new(new DbContextOptionsBuilder<InMemoryWaIngestDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options);
}
