using WaAdmin.Application.Templates.StateMachine;
using Xunit;

namespace WaAdmin.Tests.StateMachine;

public class TemplateAutoPauseScheduleTests
{
    [Fact]
    public void DurationFor_FirstPause_Is3Hours()
    {
        Assert.Equal(TimeSpan.FromHours(3), TemplateAutoPauseSchedule.DurationFor(1));
    }

    [Fact]
    public void DurationFor_SecondPause_Is6Hours()
    {
        Assert.Equal(TimeSpan.FromHours(6), TemplateAutoPauseSchedule.DurationFor(2));
    }

    [Fact]
    public void DurationFor_ThirdPause_IsNull()
    {
        // Meta's own escalation moves to DISABLED on the 3rd occurrence rather than a further
        // timed pause — see the type's doc comment.
        Assert.Null(TemplateAutoPauseSchedule.DurationFor(3));
    }
}
