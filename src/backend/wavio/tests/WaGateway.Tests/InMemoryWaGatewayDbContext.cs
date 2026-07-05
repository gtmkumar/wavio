using WaGateway.Application.Common.Interfaces;
using wavio.SharedDataModel.Entities.Messaging;
using wavio.SharedDataModel.Entities.Quality;
using wavio.SharedDataModel.Entities.Waba;
using Microsoft.EntityFrameworkCore;

namespace WaGateway.Tests;

/// <summary>
/// Minimal <see cref="IWaGatewayDbContext"/> stand-in for unit tests (same reasoning as
/// WaIngest.Tests'/WaIntel.Tests' equivalents: <c>DbSet&lt;T&gt;</c> cannot be hand-faked). Does
/// NOT enforce the <c>outbound_messages_tenant_id_idempotency_key_key</c> partial unique index
/// or RLS — the concurrent-duplicate race and cross-tenant isolation are proven only by live
/// verification against real Postgres (see the issue #14 decisions memory).
/// </summary>
public sealed class InMemoryWaGatewayDbContext : DbContext, IWaGatewayDbContext
{
    public InMemoryWaGatewayDbContext(DbContextOptions<InMemoryWaGatewayDbContext> options) : base(options) { }

    public DbSet<OutboundMessage> OutboundMessages => Set<OutboundMessage>();
    public DbSet<OutboundOutboxEntry> OutboundOutboxEntries => Set<OutboundOutboxEntry>();
    public DbSet<WabaPhoneNumber> WabaPhoneNumbers => Set<WabaPhoneNumber>();
    public DbSet<GuardianIncident> GuardianIncidents => Set<GuardianIncident>();
    public DbSet<SuppressionListEntry> SuppressionListEntries => Set<SuppressionListEntry>();

    public static InMemoryWaGatewayDbContext Create(string databaseName) =>
        new(new DbContextOptionsBuilder<InMemoryWaGatewayDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options);
}
