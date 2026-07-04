using Microsoft.Extensions.Logging.Abstractions;
using WaGateway.Infrastructure.RateLimiting;
using Xunit;

namespace WaGateway.Tests.RateLimiting;

public class MessagingTierGateTests
{
    [Fact]
    public void New_unique_recipients_are_allowed_up_to_the_tier_limit()
    {
        var gate = new MessagingTierGate(NullLogger<MessagingTierGate>.Instance);
        var phoneNumberId = Guid.NewGuid();

        for (var i = 0; i < 3; i++)
        {
            Assert.True(gate.TryRegister(phoneNumberId, $"91900000{i:D4}", tierLimitPerDay: 3));
        }
    }

    [Fact]
    public void A_new_unique_recipient_beyond_the_tier_limit_is_rejected()
    {
        var gate = new MessagingTierGate(NullLogger<MessagingTierGate>.Instance);
        var phoneNumberId = Guid.NewGuid();

        for (var i = 0; i < 3; i++)
        {
            Assert.True(gate.TryRegister(phoneNumberId, $"91900000{i:D4}", tierLimitPerDay: 3));
        }

        Assert.False(gate.TryRegister(phoneNumberId, "9190000099", tierLimitPerDay: 3));
    }

    [Fact]
    public void An_already_counted_recipient_never_counts_against_the_limit_again()
    {
        var gate = new MessagingTierGate(NullLogger<MessagingTierGate>.Instance);
        var phoneNumberId = Guid.NewGuid();
        const string recipient = "9190000001";

        Assert.True(gate.TryRegister(phoneNumberId, recipient, tierLimitPerDay: 1));
        // Same recipient again — should still succeed even though the tier is "full" of 1 unique
        // recipient, because it's the SAME recipient, not a new one.
        Assert.True(gate.TryRegister(phoneNumberId, recipient, tierLimitPerDay: 1));
    }

    [Fact]
    public void A_non_positive_tier_limit_means_unlimited()
    {
        var gate = new MessagingTierGate(NullLogger<MessagingTierGate>.Instance);
        var phoneNumberId = Guid.NewGuid();

        for (var i = 0; i < 10; i++)
        {
            Assert.True(gate.TryRegister(phoneNumberId, $"9190000{i:D4}", tierLimitPerDay: 0));
        }
    }

    [Fact]
    public void Tiers_are_independent_per_phone_number()
    {
        var gate = new MessagingTierGate(NullLogger<MessagingTierGate>.Instance);
        var phoneA = Guid.NewGuid();
        var phoneB = Guid.NewGuid();

        Assert.True(gate.TryRegister(phoneA, "9190000001", tierLimitPerDay: 1));
        Assert.False(gate.TryRegister(phoneA, "9190000002", tierLimitPerDay: 1));

        // phoneB's tier is untouched by phoneA's exhaustion.
        Assert.True(gate.TryRegister(phoneB, "9190000002", tierLimitPerDay: 1));
    }
}
