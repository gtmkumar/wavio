using WaIntel.Infrastructure.BackgroundWork;
using Xunit;

namespace WaIntel.Tests.BackgroundWork;

public class HealthSnapshotRollupServiceTests
{
    [Fact]
    public void ComputeMostRecentCompletedWeek_OnAWednesday_ReturnsPriorMondayThroughSunday()
    {
        // 2026-06-24 is a Wednesday.
        var now = new DateTimeOffset(2026, 6, 24, 10, 0, 0, TimeSpan.Zero);

        var (start, end) = HealthSnapshotRollupService.ComputeMostRecentCompletedWeek(now);

        Assert.Equal(new DateOnly(2026, 6, 15), start); // the Monday before this week's Monday
        Assert.Equal(new DateOnly(2026, 6, 21), end);   // the Sunday just before "now"'s week
    }

    [Fact]
    public void ComputeMostRecentCompletedWeek_OnAMonday_StillReturnsThePriorFullWeek_NotTheJustStartedOne()
    {
        // 2026-06-22 is a Monday — the currently-starting week must NOT be rolled up yet.
        var now = new DateTimeOffset(2026, 6, 22, 0, 30, 0, TimeSpan.Zero);

        var (start, end) = HealthSnapshotRollupService.ComputeMostRecentCompletedWeek(now);

        Assert.Equal(new DateOnly(2026, 6, 15), start);
        Assert.Equal(new DateOnly(2026, 6, 21), end);
    }

    [Fact]
    public void ComputeMostRecentCompletedWeek_PeriodIsAlwaysExactlySevenDays()
    {
        var (start, end) = HealthSnapshotRollupService.ComputeMostRecentCompletedWeek(DateTimeOffset.UtcNow);

        Assert.Equal(6, end.DayNumber - start.DayNumber);
    }
}
