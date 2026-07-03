using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Wavio.Utilities.CQRS.Behaviors;

/// <summary>
/// Opt-in marker for a query whose result can be served from cache. Implement it on a query and
/// supply a stable <see cref="CacheKey"/> plus a time-to-live.
/// </summary>
public interface ICacheableRequest
{
    string CacheKey { get; }

    TimeSpan? Ttl => null;
}

/// <summary>
/// Short-circuits requests implementing <see cref="ICacheableRequest"/> by returning a cached result
/// when present, otherwise running the pipeline and caching the produced value.
/// </summary>
public sealed class CachingBehavior<TRequest, TResult>
    : IPipelineBehavior<TRequest, TResult>
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    private readonly IMemoryCache _cache;
    private readonly ILogger<CachingBehavior<TRequest, TResult>> _logger;

    public CachingBehavior(
        IMemoryCache cache,
        ILogger<CachingBehavior<TRequest, TResult>> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<TResult> HandleAsync(
        TRequest request,
        CancellationToken cancellationToken,
        Func<Task<TResult>> next)
    {
        if (request is not ICacheableRequest cacheable)
        {
            return await next();
        }

        if (_cache.TryGetValue(cacheable.CacheKey, out TResult? cached) && cached is not null)
        {
            _logger.LogDebug(
                "Cache hit for {Request} ({Key})",
                typeof(TRequest).Name,
                cacheable.CacheKey);

            return cached;
        }

        var result = await next();

        _cache.Set(
            cacheable.CacheKey,
            result,
            cacheable.Ttl ?? DefaultTtl);

        return result;
    }
}
