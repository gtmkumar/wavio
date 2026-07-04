using WaGateway.Application.Messages.Dtos;

namespace WaGateway.Application.Messages.Logic;

public enum SendDecision
{
    Allow,

    /// <summary>Free-form send with no open CS/CTWA window (ADR-005). NEVER silently converted to
    /// a template — the caller must explicitly resend as a template.</summary>
    RejectWindowClosed,
}

public sealed record WindowPolicyResult(SendDecision Decision, bool? BillableEstimate);

/// <summary>
/// Pure implementation of the window-aware send policy (spec §4.2, ADR-005) — no DB, no I/O, so
/// every window/category combination can be unit tested directly.
///
/// Rules:
///  - Free-form (any non-template type): allowed only if a CS or CTWA window is open; otherwise
///    rejected with <see cref="SendDecision.RejectWindowClosed"/> — never silently turned into a
///    template send.
///  - Template, marketing category: always allowed, always billable.
///  - Template, authentication category: always allowed, always billable (never free in-window —
///    spec §2.2's free-traffic table is explicit that auth templates are the one template
///    category NOT covered by an open window).
///  - Template, utility category: always allowed; billable only when NO window is open (free
///    in-window, same as free-form).
/// </summary>
public static class WindowPolicyEvaluator
{
    public static WindowPolicyResult Evaluate(
        string messageType, string? templateCategory, bool csWindowOpen, bool ctwaWindowOpen)
    {
        var windowOpen = csWindowOpen || ctwaWindowOpen;

        if (MessageTypes.IsFreeForm(messageType))
        {
            return windowOpen
                ? new WindowPolicyResult(SendDecision.Allow, BillableEstimate: false)
                : new WindowPolicyResult(SendDecision.RejectWindowClosed, BillableEstimate: null);
        }

        return templateCategory switch
        {
            "marketing" => new WindowPolicyResult(SendDecision.Allow, BillableEstimate: true),
            "authentication" => new WindowPolicyResult(SendDecision.Allow, BillableEstimate: true),
            "utility" => new WindowPolicyResult(SendDecision.Allow, BillableEstimate: !windowOpen),
            _ => new WindowPolicyResult(SendDecision.Allow, BillableEstimate: null),
        };
    }
}
