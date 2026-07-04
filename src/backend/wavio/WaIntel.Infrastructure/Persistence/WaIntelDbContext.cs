using WaIntel.Application.Common.Interfaces;
using wavio.SharedDataModel.Entities.Sessions;
using wavio.SharedDataModel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace WaIntel.Infrastructure.Persistence;

/// <summary>
/// Adapts the shared <see cref="WavioDbContext"/> to <see cref="IWaIntelDbContext"/>, exposing
/// only the <c>sessions</c> schema entity sets this service owns. Same pattern as
/// core.Infrastructure's <c>CoreDbContext</c> / WaIngest.Infrastructure's <c>WaIngestDbContext</c>.
/// </summary>
public sealed class WaIntelDbContext : IWaIntelDbContext
{
    private readonly WavioDbContext _db;

    public WaIntelDbContext(WavioDbContext db) => _db = db;

    public DbSet<ConversationWindow> ConversationWindows => _db.ConversationWindows;
    public DbSet<WindowEvent> WindowEvents => _db.WindowEvents;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        _db.SaveChangesAsync(cancellationToken);

    /// <inheritdoc />
    public async Task NotifyAsync(string channel, string payload, CancellationToken cancellationToken)
    {
        // pg_notify's channel argument can't be parameterized as an identifier in all EF
        // providers, but it's never caller-supplied (always one of our two literal channel
        // names) — the payload is the only parameterized value.
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_notify({channel}, {payload})", cancellationToken);
    }

    /// <inheritdoc />
    public async Task SetTenantContextAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        // EF Core opens/closes its underlying connection implicitly around each individual
        // operation unless something holds it open explicitly — and RlsConnectionInterceptor
        // re-runs (resetting this GUC back to ICurrentTenant's, i.e. empty, outside an HTTP
        // request) on every one of those opens. OpenConnectionAsync makes the connection stay
        // open for the rest of this DbContext's lifetime, so the interceptor only fires once
        // (before this override) and our explicit value survives every subsequent command.
        await _db.Database.OpenConnectionAsync(cancellationToken);

        var tenantIdText = tenantId.ToString();
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.tenant_id', {tenantIdText}, false)", cancellationToken);
    }
}
