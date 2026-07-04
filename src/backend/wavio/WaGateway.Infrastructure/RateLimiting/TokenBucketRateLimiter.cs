using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace WaGateway.Infrastructure.RateLimiting;

/// <summary>
/// Per-phone-number token bucket, calibrated from Meta's throughput field (spec §4.2: default 80
/// MPS, auto-upgradable to 1,000 MPS — <see cref="Graph.MetaGraphOptions.DefaultThroughputPerSecond"/>
/// for now, since the real per-number value arrives with onboarding, issue #6). Registered
/// Singleton — bucket state is in-memory and therefore per-instance, not cluster-wide; a
/// multi-instance deployment would need a shared store (Redis token bucket or similar), which is
/// out of Wave 1 scope (see the issue #14 decisions memory).
/// </summary>
public sealed partial class TokenBucketRateLimiter
{
    private sealed class Bucket
    {
        public double Tokens;
        public long LastRefillTicks;
    }

    private readonly ConcurrentDictionary<Guid, Bucket> _buckets = new();
    private readonly ILogger<TokenBucketRateLimiter> _logger;

    public TokenBucketRateLimiter(ILogger<TokenBucketRateLimiter> logger) => _logger = logger;

    /// <summary>True if a token was available and consumed (send may proceed); false if the
    /// phone number's bucket is empty (send should be throttled/delayed).</summary>
    public bool TryConsume(Guid phoneNumberId, int capacityPerSecond)
    {
        var now = DateTime.UtcNow.Ticks;
        var bucket = _buckets.GetOrAdd(phoneNumberId, _ => new Bucket { Tokens = capacityPerSecond, LastRefillTicks = now });

        lock (bucket)
        {
            var elapsedSeconds = (now - bucket.LastRefillTicks) / (double)TimeSpan.TicksPerSecond;
            if (elapsedSeconds > 0)
            {
                bucket.Tokens = Math.Min(capacityPerSecond, bucket.Tokens + elapsedSeconds * capacityPerSecond);
                bucket.LastRefillTicks = now;
            }

            if (bucket.Tokens < 1)
            {
                LogThrottled(_logger, phoneNumberId, capacityPerSecond);
                return false;
            }

            bucket.Tokens -= 1;
            return true;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Throttled send for phone number {PhoneNumberId}: token bucket empty (capacity {CapacityPerSecond}/s)")]
    private static partial void LogThrottled(ILogger logger, Guid phoneNumberId, int capacityPerSecond);
}
