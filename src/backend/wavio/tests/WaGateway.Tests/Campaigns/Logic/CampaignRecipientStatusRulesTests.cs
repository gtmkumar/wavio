using WaGateway.Application.Campaigns.Logic;
using Xunit;

namespace WaGateway.Tests.Campaigns.Logic;

public class CampaignRecipientStatusRulesTests
{
    [Theory]
    [InlineData("sent", "delivered", true)]
    [InlineData("sent", "read", true)]
    [InlineData("delivered", "read", true)]
    [InlineData("read", "delivered", false)] // regression — reject
    [InlineData("delivered", "delivered", false)] // redelivered no-op
    [InlineData("read", "read", false)]
    public void IsForwardTransition_only_allows_moving_forward_on_the_main_line(string current, string incoming, bool expected)
    {
        Assert.Equal(expected, CampaignRecipientStatusRules.IsForwardTransition(current, incoming));
    }

    [Fact]
    public void IsForwardTransition_a_terminal_current_status_is_never_a_forward_transition()
    {
        Assert.False(CampaignRecipientStatusRules.IsForwardTransition("failed", "delivered"));
        Assert.False(CampaignRecipientStatusRules.IsForwardTransition("cancelled", "read"));
        Assert.False(CampaignRecipientStatusRules.IsForwardTransition("suppressed", "delivered"));
    }

    [Theory]
    [InlineData("pending", true)]
    [InlineData("sent", true)]
    [InlineData("delivered", true)]
    [InlineData("read", true)]
    [InlineData("failed", false)]
    [InlineData("cancelled", false)]
    [InlineData("suppressed", false)]
    public void CanTransitionToFailed_only_from_a_non_terminal_status(string current, bool expected)
    {
        Assert.Equal(expected, CampaignRecipientStatusRules.CanTransitionToFailed(current));
    }

    [Fact]
    public void IsCampaignComplete_true_only_when_no_pending_or_sent_recipients_remain()
    {
        Assert.True(CampaignRecipientStatusRules.IsCampaignComplete(0));
        Assert.False(CampaignRecipientStatusRules.IsCampaignComplete(1));
    }
}
