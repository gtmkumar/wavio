using Microsoft.Extensions.Logging.Abstractions;
using WaGateway.Infrastructure.RateLimiting;
using Xunit;

namespace WaGateway.Tests.RateLimiting;

public class TokenBucketRateLimiterTests
{
    [Fact]
    public void First_call_up_to_capacity_succeeds()
    {
        var limiter = new TokenBucketRateLimiter(NullLogger<TokenBucketRateLimiter>.Instance);
        var phoneNumberId = Guid.NewGuid();

        for (var i = 0; i < 5; i++)
        {
            Assert.True(limiter.TryConsume(phoneNumberId, capacityPerSecond: 5));
        }
    }

    [Fact]
    public void Exceeding_capacity_within_the_same_instant_is_throttled()
    {
        var limiter = new TokenBucketRateLimiter(NullLogger<TokenBucketRateLimiter>.Instance);
        var phoneNumberId = Guid.NewGuid();

        for (var i = 0; i < 3; i++)
        {
            Assert.True(limiter.TryConsume(phoneNumberId, capacityPerSecond: 3));
        }

        // The bucket started full (capacity) and all 3 tokens were just spent — essentially no
        // time has elapsed, so there's nothing to refill yet.
        Assert.False(limiter.TryConsume(phoneNumberId, capacityPerSecond: 3));
    }

    [Fact]
    public void Buckets_are_independent_per_phone_number()
    {
        var limiter = new TokenBucketRateLimiter(NullLogger<TokenBucketRateLimiter>.Instance);
        var phoneA = Guid.NewGuid();
        var phoneB = Guid.NewGuid();

        Assert.True(limiter.TryConsume(phoneA, capacityPerSecond: 1));
        Assert.False(limiter.TryConsume(phoneA, capacityPerSecond: 1));

        // A different phone number's bucket is unaffected by phoneA's exhaustion.
        Assert.True(limiter.TryConsume(phoneB, capacityPerSecond: 1));
    }

    [Fact]
    public async Task Tokens_refill_over_time()
    {
        var limiter = new TokenBucketRateLimiter(NullLogger<TokenBucketRateLimiter>.Instance);
        var phoneNumberId = Guid.NewGuid();

        Assert.True(limiter.TryConsume(phoneNumberId, capacityPerSecond: 1));
        Assert.False(limiter.TryConsume(phoneNumberId, capacityPerSecond: 1));

        await Task.Delay(TimeSpan.FromSeconds(1.1));

        Assert.True(limiter.TryConsume(phoneNumberId, capacityPerSecond: 1));
    }
}
