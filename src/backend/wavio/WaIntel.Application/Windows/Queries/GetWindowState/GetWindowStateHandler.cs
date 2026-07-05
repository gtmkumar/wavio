using WaIntel.Application.Common.Interfaces;
using WaIntel.Application.Windows.Dtos;
using WaIntel.Application.Windows.Logic;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace WaIntel.Application.Windows.Queries.GetWindowState;

/// <summary>
/// Fast-lookup: Postgres + in-memory cache, LISTEN/NOTIFY invalidation across instances (issue
/// #15, "DocSlot RBAC resolver pattern"). NOTE: the custom CQRS dispatcher
/// (Wavio.Utilities.CQRS.Dispatcher.Dispatcher) does not actually invoke
/// <c>IPipelineBehavior&lt;,&gt;</c> — <c>Behaviors/CachingBehavior.cs</c> exists in the shared
/// library but is unwired scaffolding, not a working caching layer. Caching is done directly
/// here instead of relying on that.
///
/// Cache invalidation on write is handled by <c>WindowCacheInvalidationListener</c>
/// (WaIntel.Infrastructure) — a Postgres LISTEN client that calls
/// <see cref="IMemoryCache.Remove"/> for the affected key the moment a NOTIFY arrives, so the TTL
/// below is a safety net, not the primary invalidation mechanism.
/// </summary>
public sealed class GetWindowStateHandler : IQueryHandler<GetWindowStateQuery, WindowStateDto?>
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly IWaIntelDbContext _db;
    private readonly IMemoryCache _cache;

    public GetWindowStateHandler(IWaIntelDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    /// <summary>
    /// The cache key intentionally ignores PhoneNumberId (see the query's doc comment) — this is
    /// also the exact key format <c>WindowCacheInvalidationListener</c> must reproduce from a
    /// NOTIFY payload to correctly evict on write.
    /// </summary>
    public static string BuildCacheKey(Guid tenantId, string userWaId) => $"window:{tenantId}:{userWaId}";

    public async Task<WindowStateDto?> HandleAsync(GetWindowStateQuery query, CancellationToken cancellationToken)
    {
        var cacheKey = BuildCacheKey(query.TenantId, query.UserWaId);

        if (_cache.TryGetValue(cacheKey, out WindowStateDto? cached))
        {
            return cached;
        }

        var windowsQuery = _db.ConversationWindows
            .AsNoTracking()
            .Where(w => w.TenantId == query.TenantId && w.UserWaId == query.UserWaId);

        if (query.PhoneNumberId.HasValue)
        {
            windowsQuery = windowsQuery.Where(w => w.PhoneNumberId == query.PhoneNumberId.Value);
        }

        var window = await windowsQuery
            .OrderByDescending(w => w.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (window is null)
        {
            // Deliberately not caching misses: a window that doesn't exist yet could be created
            // moments later by the consumer, and a cached "not found" would hide that until TTL.
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var dto = new WindowStateDto(
            window.UserWaId,
            window.PhoneNumberId,
            window.Origin,
            window.CsExpiresAt,
            WindowRules.IsOpen(window.CsExpiresAt, now),
            window.CtwaExpiresAt,
            WindowRules.IsOpen(window.CtwaExpiresAt, now));

        _cache.Set(cacheKey, dto, CacheTtl);

        return dto;
    }
}
