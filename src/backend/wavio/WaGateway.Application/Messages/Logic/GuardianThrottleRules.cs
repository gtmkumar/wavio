namespace WaGateway.Application.Messages.Logic;

/// <summary>
/// Pure Guardian throttle-enforcement rules for the outbox dispatcher (issue #20, spec §4.6). No
/// I/O — the dispatcher loads the phone number's current open incident's <c>throttle_action</c>
/// and passes it in.
///
/// A local copy of the throttle-action vocabulary rather than a shared reference to WaIntel's
/// <c>GuardianRules</c> — same cross-service duplication convention already established for
/// <c>ITenantResolver</c>/<c>WabaPhoneNumberTenantResolver</c> (each service owns its own copy of
/// the small amount of shared vocabulary rather than taking a project reference across the
/// bounded-context boundary).
///
/// Never-block rule (spec §4.6, same shape as WaBilling's <c>QuotaRules.ShouldBlock</c>, issue
/// #19): Guardian throttling applies ONLY to marketing sends — utility, authentication and service
/// messages always proceed regardless of throttle state.
/// </summary>
public static class GuardianThrottleRules
{
    public const string MarketingCategory = "marketing";
    public const string ThrottleNone = "none";
    public const string Throttle50Pct = "marketing_50pct";
    public const string ThrottleFrozen = "marketing_frozen";

    public static bool AppliesTo(string sendCategory) =>
        string.Equals(sendCategory, MarketingCategory, StringComparison.Ordinal);

    public static bool IsFrozen(string? throttleAction) =>
        string.Equals(throttleAction, ThrottleFrozen, StringComparison.Ordinal);

    public static bool IsHalved(string? throttleAction) =>
        string.Equals(throttleAction, Throttle50Pct, StringComparison.Ordinal);
}
