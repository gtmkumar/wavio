using WaIntel.Application.Windows.Logic;
using Xunit;

namespace WaIntel.Tests.Windows;

public class WindowRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CalculateCsExpiry_returns_exactly_24_hours_after_sentAt()
    {
        var expiry = WindowRules.CalculateCsExpiry(Now);

        Assert.Equal(Now + TimeSpan.FromHours(24), expiry);
    }

    [Fact]
    public void CalculateCtwaExpiry_returns_exactly_72_hours_after_enteredAt()
    {
        var expiry = WindowRules.CalculateCtwaExpiry(Now);

        Assert.Equal(Now + TimeSpan.FromHours(72), expiry);
    }

    [Theory]
    [InlineData(-1, false)] // expired 1 second ago
    [InlineData(0, false)]  // expires exactly now — not open (strictly in the future required)
    [InlineData(1, true)]   // expires 1 second from now
    public void IsOpen_boundary_at_the_exact_expiry_instant(int secondsFromNow, bool expectedOpen)
    {
        var expiresAt = Now + TimeSpan.FromSeconds(secondsFromNow);

        Assert.Equal(expectedOpen, WindowRules.IsOpen(expiresAt, Now));
    }

    [Fact]
    public void IsOpen_with_no_expiry_at_all_is_false()
    {
        Assert.False(WindowRules.IsOpen(null, Now));
    }

    [Fact]
    public void IsApproachingClose_true_when_expiry_falls_within_the_horizon_and_not_yet_notified()
    {
        var expiresAt = Now + TimeSpan.FromHours(1); // within a 2h horizon

        Assert.True(WindowRules.IsApproachingClose(expiresAt, closingNotifiedAt: null, Now, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void IsApproachingClose_false_when_expiry_is_beyond_the_horizon()
    {
        var expiresAt = Now + TimeSpan.FromHours(3); // outside a 2h horizon

        Assert.False(WindowRules.IsApproachingClose(expiresAt, closingNotifiedAt: null, Now, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void IsApproachingClose_false_when_there_is_no_expiry()
    {
        Assert.False(WindowRules.IsApproachingClose(null, closingNotifiedAt: null, Now, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void IsApproachingClose_double_notify_guard_false_once_already_notified()
    {
        var expiresAt = Now + TimeSpan.FromMinutes(30);
        var alreadyNotifiedAt = Now - TimeSpan.FromMinutes(5);

        Assert.False(WindowRules.IsApproachingClose(expiresAt, alreadyNotifiedAt, Now, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void IsApproachingClose_true_again_after_the_notify_guard_is_reset_to_null()
    {
        // Simulates what UpsertWindowOnMessageReceivedHandler does when a window is extended:
        // closing_notified_at is cleared, so a re-opened/extended window becomes eligible again.
        var expiresAt = Now + TimeSpan.FromMinutes(30);

        Assert.True(WindowRules.IsApproachingClose(expiresAt, closingNotifiedAt: null, Now, TimeSpan.FromHours(2)));
    }
}
