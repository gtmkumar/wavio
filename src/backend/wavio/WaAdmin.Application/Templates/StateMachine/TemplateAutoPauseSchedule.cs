namespace WaAdmin.Application.Templates.StateMachine;

/// <summary>
/// Meta's documented auto-pause escalation for quality issues (spec §4.4, issue #16 Task 5):
/// 1st pause = 3 hours, 2nd pause = 6 hours, 3rd occurrence -&gt; Meta disables the template
/// outright (communicated as its own DISABLED status event, not a 3rd pause window).
/// </summary>
public static class TemplateAutoPauseSchedule
{
    /// <param name="pauseCountAfterIncrement">Template.PauseCount AFTER incrementing for this
    /// pause occurrence (i.e. 1 for the first pause ever seen, 2 for the second, ...).</param>
    /// <returns>How long the pause should last; null once escalation is expected to have moved
    /// to DISABLED rather than a further timed pause.</returns>
    public static TimeSpan? DurationFor(short pauseCountAfterIncrement) => pauseCountAfterIncrement switch
    {
        1 => TimeSpan.FromHours(3),
        2 => TimeSpan.FromHours(6),
        _ => null,
    };
}
