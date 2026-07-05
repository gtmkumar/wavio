using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace WaGateway.Infrastructure.RateLimiting;

/// <summary>
/// Enforces a Guardian <c>marketing_50pct</c> throttle (issue #20, spec §4.6: YELLOW quality cuts
/// marketing velocity in half) by deterministically allowing every OTHER attempt per phone number,
/// rather than a probabilistic 50% coin-flip — this makes the "cut to half velocity" behavior
/// exact and trivially unit-testable (no statistical assertions needed), at the cost of not being
/// truly random; that trade-off is fine for a velocity throttle, unlike e.g. a security control.
/// Singleton, in-memory, per-instance — same Wave 1 limitation class as
/// <see cref="MessagingTierGate"/>/<see cref="TokenBucketRateLimiter"/> (issue #14 decisions memory).
/// </summary>
public sealed partial class GuardianThrottleGate
{
    private readonly ConcurrentDictionary<Guid, int> _attemptCounts = new();
    private readonly ILogger<GuardianThrottleGate> _logger;

    public GuardianThrottleGate(ILogger<GuardianThrottleGate> logger) => _logger = logger;

    /// <summary>True on the 1st, 3rd, 5th... attempt for this phone number since the throttle
    /// started being checked; false on the 2nd, 4th, 6th... — a clean 50% cut over any window of
    /// attempts, without needing to track a rolling time window.</summary>
    public bool TryAllowHalvedSend(Guid phoneNumberId)
    {
        var count = _attemptCounts.AddOrUpdate(phoneNumberId, 1, (_, c) => c + 1);
        var allowed = count % 2 == 1;
        if (!allowed)
        {
            LogHalvedSendSkipped(_logger, phoneNumberId);
        }
        return allowed;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Guardian marketing_50pct throttle: skipping this attempt for phone number {PhoneNumberId} (every other attempt is held back for retry)")]
    private static partial void LogHalvedSendSkipped(ILogger logger, Guid phoneNumberId);
}
