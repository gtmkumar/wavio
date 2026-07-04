using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace WaGateway.Infrastructure.RateLimiting;

/// <summary>
/// Messaging-tier headroom (spec §4.2: unique marketing-initiated recipients per rolling 24h —
/// 250 → 1K → 10K → 100K → unlimited). Only applies to MARKETING template sends (utility/service
/// messages never count against it, per spec). Registered Singleton — in-memory and per-instance,
/// same Wave 1 limitation as <see cref="TokenBucketRateLimiter"/> (see issue #14 decisions memory).
/// A tier value &lt;= 0 means unlimited (skip the check entirely).
/// </summary>
public sealed partial class MessagingTierGate
{
    private sealed class RecipientWindow
    {
        public readonly ConcurrentDictionary<string, DateTimeOffset> LastSeenByWaId = new();
    }

    private readonly ConcurrentDictionary<Guid, RecipientWindow> _windows = new();
    private readonly ILogger<MessagingTierGate> _logger;

    public MessagingTierGate(ILogger<MessagingTierGate> logger) => _logger = logger;

    /// <summary>True if this send is within tier headroom (already-counted recipients never
    /// block; only a NEW unique recipient can push over the limit). Registers the recipient as
    /// counted for the next 24h as a side effect of an allowed call.</summary>
    public bool TryRegister(Guid phoneNumberId, string toWaId, int tierLimitPerDay)
    {
        if (tierLimitPerDay <= 0)
        {
            return true; // unlimited tier
        }

        var window = _windows.GetOrAdd(phoneNumberId, _ => new RecipientWindow());
        var now = DateTimeOffset.UtcNow;
        var cutoff = now.AddHours(-24);

        // Prune stale entries opportunistically (bounded cost — only runs on the calling
        // thread's own phone number bucket, not a global sweep).
        foreach (var (waId, lastSeen) in window.LastSeenByWaId)
        {
            if (lastSeen < cutoff)
            {
                window.LastSeenByWaId.TryRemove(waId, out _);
            }
        }

        if (window.LastSeenByWaId.ContainsKey(toWaId))
        {
            window.LastSeenByWaId[toWaId] = now; // refresh, doesn't count against the limit again
            return true;
        }

        if (window.LastSeenByWaId.Count >= tierLimitPerDay)
        {
            LogTierExhausted(_logger, phoneNumberId, tierLimitPerDay);
            return false;
        }

        window.LastSeenByWaId[toWaId] = now;
        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Messaging-tier headroom exhausted for phone number {PhoneNumberId} (limit {TierLimitPerDay} unique recipients/24h)")]
    private static partial void LogTierExhausted(ILogger logger, Guid phoneNumberId, int tierLimitPerDay);
}
