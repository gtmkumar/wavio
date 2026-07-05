using WaIntel.Application.Common.Interfaces;
using wavio.SharedDataModel.Entities.Quality;
using wavio.SharedDataModel.Entities.Sessions;
using wavio.SharedDataModel.Entities.Waba;
using Microsoft.EntityFrameworkCore;

namespace WaIntel.Tests;

/// <summary>
/// Minimal <see cref="IWaIntelDbContext"/> stand-in for unit tests (same reasoning as
/// WaIngest.Tests' <c>InMemoryWaIngestDbContext</c>: <c>DbSet&lt;T&gt;</c> cannot be hand-faked).
/// <see cref="NotifyAsync"/> can't hit real Postgres <c>pg_notify</c> against the in-memory
/// provider — it just records calls so tests can assert a notification was (or wasn't) sent.
/// </summary>
public sealed class InMemoryWaIntelDbContext : DbContext, IWaIntelDbContext
{
    public List<(string Channel, string Payload)> Notifications { get; } = [];

    public InMemoryWaIntelDbContext(DbContextOptions<InMemoryWaIntelDbContext> options) : base(options) { }

    public DbSet<ConversationWindow> ConversationWindows => Set<ConversationWindow>();
    public DbSet<WindowEvent> WindowEvents => Set<WindowEvent>();
    public DbSet<WabaPhoneNumber> WabaPhoneNumbers => Set<WabaPhoneNumber>();

    public DbSet<NumberQualityEvent> NumberQualityEvents => Set<NumberQualityEvent>();
    public DbSet<MessagingTierEvent> MessagingTierEvents => Set<MessagingTierEvent>();
    public DbSet<GuardianIncident> GuardianIncidents => Set<GuardianIncident>();
    public DbSet<HealthSnapshot> HealthSnapshots => Set<HealthSnapshot>();

    public Task NotifyAsync(string channel, string payload, CancellationToken cancellationToken)
    {
        Notifications.Add((channel, payload));
        return Task.CompletedTask;
    }

    // The in-memory provider doesn't enforce RLS at all, so there's no GUC to set — this exists
    // only to satisfy the interface.
    public Task SetTenantContextAsync(Guid tenantId, CancellationToken cancellationToken) => Task.CompletedTask;

    public static InMemoryWaIntelDbContext Create(string databaseName) =>
        new(new DbContextOptionsBuilder<InMemoryWaIntelDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options);
}
