using wavio.SharedDataModel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace wavio.SharedDataModel.Persistence;

/// <summary>
/// <see cref="ITokenVersionStore"/> backed by the <c>kernel.user_perm_version(uuid)</c>
/// SECURITY DEFINER function (reads through RLS) plus a short in-process cache. The cache
/// TTL bounds revocation latency (a bump applies within the TTL) while keeping the per-request
/// cost to ~one lookup per user per TTL per process. Bumps happen in the core host, so other
/// processes converge within the TTL (no cross-process invalidation needed at this scale).
/// </summary>
public sealed class TokenVersionStore : ITokenVersionStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(15);

    private readonly WavioDbContext _db;
    private readonly IMemoryCache _cache;

    public TokenVersionStore(WavioDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<int?> GetPermVersionAsync(Guid userId, CancellationToken ct)
    {
        var key = $"permver:{userId}";
        if (_cache.TryGetValue(key, out int cached)) return cached;

        try
        {
            var rows = await _db.Database
                .SqlQuery<int?>($"SELECT kernel.user_perm_version({userId}) AS \"Value\"")
                .ToListAsync(ct);
            var val = rows.Count > 0 ? rows[0] : null;
            if (val.HasValue) _cache.Set(key, val.Value, Ttl);
            return val;
        }
        catch
        {
            // Fail open: never block a request because the version lookup errored.
            return null;
        }
    }
}
