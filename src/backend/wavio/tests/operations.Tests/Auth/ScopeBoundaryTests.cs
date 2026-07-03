using wavio.SharedDataModel.Enums;
using wavio.Utilities.Services;
using Xunit;

namespace operations.Tests.Auth;

/// <summary>
/// Locks in the ancestor-or-self boundary check exposed by <c>HttpContextCurrentUser.IsWithinScope</c>.
/// The claim layer is EXACT-level (each node only matches its own level); wider platform scope
/// short-circuits to true for every target.
/// </summary>
public class ScopeBoundaryTests
{
    private static ICurrentUser CurrentUser(
        string? scopeNodes = null, string? userType = null, string? scopeType = null)
        => new HttpContextCurrentUser(
            RbacTestSupport.AccessorFor(
                RbacTestSupport.Principal(
                    tokenUse: "user", userType: userType, scopeNodes: scopeNodes, scopeType: scopeType)));

    // Test 1 — absent scope_nodes claim fails OPEN (rollout safety); a present-but-empty claim denies.
    [Fact]
    public void Absent_scope_nodes_allows_but_empty_string_denies()
    {
        var anyTenant = Guid.NewGuid();

        // Claim omitted entirely → not enforceable → allow.
        var noClaim = CurrentUser(scopeNodes: null);
        Assert.True(noClaim.IsWithinScope(tenantId: anyTenant));

        // Claim present but empty → enforced, zero nodes → deny.
        var emptyClaim = CurrentUser(scopeNodes: "");
        Assert.False(emptyClaim.IsWithinScope(tenantId: anyTenant));
    }

    // Test 2 — a single tenant node matches ONLY that tenant; it does not widen to a sibling tenant.
    [Fact]
    public void Tenant_node_matches_exactly_that_tenant_only()
    {
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        var user = CurrentUser(scopeNodes: $"{ScopeType.Tenant}:{t1}");

        Assert.True(user.IsWithinScope(tenantId: t1));   // exact node → allow
        Assert.False(user.IsWithinScope(tenantId: t2));  // sibling tenant → deny
    }

    // Test 2 (cont.) — a platform node, and a platform_admin user_type, are unbounded.
    [Fact]
    public void Platform_scope_and_platform_admin_are_unbounded()
    {
        var target = Guid.NewGuid();

        // "platform" node → the switch returns true for every target.
        var platformNode = CurrentUser(scopeNodes: ScopeType.Platform);
        Assert.True(platformNode.IsWithinScope(tenantId: target));

        // user_type=platform_admin short-circuits IsPlatformAdmin → true for any target,
        // even with no scope_nodes claim at all.
        var platformAdmin = CurrentUser(userType: UserType.PlatformAdmin);
        Assert.True(platformAdmin.IsWithinScope(tenantId: target));
    }
}
