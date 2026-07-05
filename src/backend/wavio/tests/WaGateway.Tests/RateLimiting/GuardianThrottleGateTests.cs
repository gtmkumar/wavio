using Microsoft.Extensions.Logging.Abstractions;
using WaGateway.Infrastructure.RateLimiting;
using Xunit;

namespace WaGateway.Tests.RateLimiting;

public class GuardianThrottleGateTests
{
    [Fact]
    public void TryAllowHalvedSend_AlternatesAllowAndSkipPerPhoneNumber()
    {
        var gate = new GuardianThrottleGate(NullLogger<GuardianThrottleGate>.Instance);
        var phoneNumberId = Guid.NewGuid();

        Assert.True(gate.TryAllowHalvedSend(phoneNumberId));  // 1st: allow
        Assert.False(gate.TryAllowHalvedSend(phoneNumberId)); // 2nd: skip
        Assert.True(gate.TryAllowHalvedSend(phoneNumberId));  // 3rd: allow
        Assert.False(gate.TryAllowHalvedSend(phoneNumberId)); // 4th: skip
    }

    [Fact]
    public void TryAllowHalvedSend_OverManyAttempts_AllowsExactlyHalf()
    {
        var gate = new GuardianThrottleGate(NullLogger<GuardianThrottleGate>.Instance);
        var phoneNumberId = Guid.NewGuid();

        var allowedCount = 0;
        for (var i = 0; i < 100; i++)
        {
            if (gate.TryAllowHalvedSend(phoneNumberId)) allowedCount++;
        }

        Assert.Equal(50, allowedCount);
    }

    [Fact]
    public void TryAllowHalvedSend_CountersAreIndependentPerPhoneNumber()
    {
        var gate = new GuardianThrottleGate(NullLogger<GuardianThrottleGate>.Instance);
        var phoneA = Guid.NewGuid();
        var phoneB = Guid.NewGuid();

        Assert.True(gate.TryAllowHalvedSend(phoneA));
        Assert.False(gate.TryAllowHalvedSend(phoneA));

        // phoneB starts its own fresh count — unaffected by phoneA's state.
        Assert.True(gate.TryAllowHalvedSend(phoneB));
    }
}
