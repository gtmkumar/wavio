using wavio.Utilities.Auth;
using Xunit;

namespace operations.Tests.Auth;

/// <summary>
/// Locks in the §8 step-up primitives: which codes demand a fresh re-verification
/// (<see cref="StepUp.RequiresStepUp"/>) and whether the caller's proof is still fresh
/// (<see cref="StepUp.IsFresh"/>). Pure claim reads — no DB, no clock injection.
/// </summary>
public class StepUpTests
{
    // Test 3 — a code is "step-up required" iff its canonical form is in the signed step_up_perms claim.
    [Fact]
    public void RequiresStepUp_tracks_the_step_up_perms_claim()
    {
        var user = RbacTestSupport.Principal(stepUpPerms: "wallet.adjust orders.refund");

        Assert.True(StepUp.RequiresStepUp(user, "wallet.adjust"));   // listed → required
        Assert.False(StepUp.RequiresStepUp(user, "orders.create"));  // not listed → not required

        // Absent claim → nothing is step-up-gated.
        var noClaim = RbacTestSupport.Principal();
        Assert.False(StepUp.RequiresStepUp(noClaim, "wallet.adjust"));
    }

    // Test 3 (cont.) — a legacy code in the claim canonicalizes to the current code before comparison,
    // so an already-issued token still trips the gate for the renamed permission.
    [Fact]
    public void RequiresStepUp_canonicalizes_legacy_claim_codes()
    {
        // garment.read → fulfillment.read (active PermissionAlias bridge).
        var user = RbacTestSupport.Principal(stepUpPerms: "garment.read");
        Assert.True(StepUp.RequiresStepUp(user, "fulfillment.read"));
    }

    // Test 4 — freshness window is [0, 5min]; stale, future, and unparsable proofs all fail.
    [Fact]
    public void IsFresh_only_accepts_a_proof_within_the_window()
    {
        // Stamped "now" → age ~0s, inside the 5-minute window → fresh.
        var fresh = RbacTestSupport.Principal(stepUpAt: RbacTestSupport.UnixSecondsFromNow(TimeSpan.Zero));
        Assert.True(StepUp.IsFresh(fresh));

        // Stamped 6 minutes ago → age > window → stale.
        var stale = RbacTestSupport.Principal(stepUpAt: RbacTestSupport.UnixSecondsFromNow(TimeSpan.FromMinutes(-6)));
        Assert.False(StepUp.IsFresh(stale));

        // Stamped in the FUTURE (+1 min) → age < 0 → rejected by the age >= 0 guard.
        var future = RbacTestSupport.Principal(stepUpAt: RbacTestSupport.UnixSecondsFromNow(TimeSpan.FromMinutes(1)));
        Assert.False(StepUp.IsFresh(future));

        // Missing claim and non-numeric claim → long.TryParse fails → not fresh.
        Assert.False(StepUp.IsFresh(RbacTestSupport.Principal()));
        Assert.False(StepUp.IsFresh(RbacTestSupport.Principal(stepUpAt: "not-a-number")));
    }
}
