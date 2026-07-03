using wavio.Utilities.Auth;
using Xunit;

namespace operations.Tests.Auth;

/// <summary>
/// Guards the garment.* → fulfillment.* permission bridge (multi-vertical Phase 1 / slice E).
/// The bridge keeps already-issued JWTs (whose 'permissions' claim still carries garment.*) valid
/// against the renamed fulfillment.* endpoint policies until tokens cycle.
/// </summary>
public class PermissionAliasTests
{
    [Theory]
    [InlineData("garment.read",    "fulfillment.read")]
    [InlineData("garment.tag",     "fulfillment.tag")]
    [InlineData("garment.inspect", "fulfillment.inspect")]
    public void Canonical_maps_legacy_codes_to_current(string legacy, string current)
        => Assert.Equal(current, PermissionAlias.Canonical(legacy));

    [Theory]
    [InlineData("fulfillment.read")]   // already-current code is unchanged
    [InlineData("orders.read")]        // unrelated code is unchanged
    [InlineData("warehouse.batch.manage")]
    public void Canonical_is_identity_for_non_aliased_codes(string code)
        => Assert.Equal(code, PermissionAlias.Canonical(code));

    [Fact]
    public void Legacy_claim_satisfies_renamed_requirement_via_canonicalization()
    {
        // Simulates the handler: a token holding the legacy code must satisfy the new required code.
        const string heldLegacyClaim = "garment.read";
        const string requiredNewCode = "fulfillment.read";
        Assert.Equal(
            PermissionAlias.Canonical(requiredNewCode),
            PermissionAlias.Canonical(heldLegacyClaim));
    }
}
