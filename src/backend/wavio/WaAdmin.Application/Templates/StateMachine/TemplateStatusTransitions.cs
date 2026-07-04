namespace WaAdmin.Application.Templates.StateMachine;

/// <summary>
/// Pure, app-enforced guard for the template status state machine (spec §4.4, issue #16):
/// <code>
/// DRAFT    -&gt; PENDING                       (submitted to Meta)
/// PENDING  -&gt; APPROVED | REJECTED           (Meta review outcome)
/// APPROVED -&gt; PAUSED | DISABLED | DRAFT     (Meta quality auto-pause / kill; tenant edit)
/// PAUSED   -&gt; APPROVED | DISABLED | DRAFT   (unpaused, escalated, or tenant edit)
/// REJECTED -&gt; DRAFT                         (tenant edits and resubmits)
/// DISABLED -&gt; (terminal — no transitions out)
/// </code>
/// The three "-&gt; DRAFT" edges are locally-triggered edits (issue #16 Task 6: "post-approval
/// edits are rejected; edits create a new template_versions row (DRAFT) instead") — they never
/// come from a Meta webhook, only from UpdateTemplateCommandHandler creating a fresh version.
/// Both <c>templates.templates.status</c> and <c>templates.template_versions.status</c> share
/// this table. Every accepted transition MUST be paired with a <c>TemplateStatusEvent</c> row by
/// the caller — this class only says whether a transition is legal, it does not record anything.
/// </summary>
public static class TemplateStatusTransitions
{
    public const string Draft = "DRAFT";
    public const string Pending = "PENDING";
    public const string Approved = "APPROVED";
    public const string Rejected = "REJECTED";
    public const string Paused = "PAUSED";
    public const string Disabled = "DISABLED";

    private static readonly Dictionary<string, string[]> Allowed =
        new(StringComparer.Ordinal)
        {
            [Draft] = [Pending],
            [Pending] = [Approved, Rejected],
            [Approved] = [Paused, Disabled, Draft],
            [Paused] = [Approved, Disabled, Draft],
            [Rejected] = [Draft],
            [Disabled] = [],
        };

    /// <summary>All status values a CHECK constraint / client validator should accept.</summary>
    public static readonly IReadOnlyCollection<string> AllStatuses =
        [Draft, Pending, Approved, Rejected, Paused, Disabled];

    public static bool IsValidStatus(string status) => Allowed.ContainsKey(status);

    /// <summary>True iff moving from <paramref name="from"/> to <paramref name="to"/> is a legal
    /// single-step transition. Same-status "transitions" are never valid (callers must no-op
    /// instead of writing a redundant event).</summary>
    public static bool CanTransition(string from, string to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to, StringComparer.Ordinal);
}
