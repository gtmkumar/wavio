using System.Security.Claims;
using wavio.SharedDataModel.Enums;
using wavio.Utilities.Auth;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace operations.Tests.Auth;

/// <summary>
/// Locks in the three gates of <see cref="PermissionHandler"/>: token_use=user, membership
/// (with platform_admin bypass), and the §8 step-up gate. Distinguishes a plain miss
/// (neither succeed nor fail — other handlers may satisfy) from a typed step-up Fail.
/// </summary>
public class PermissionHandlerTests
{
    private static async Task<AuthorizationHandlerContext> Evaluate(ClaimsPrincipal user, string requiredCode)
    {
        var requirement = new PermissionRequirement(requiredCode);
        var context = new AuthorizationHandlerContext(new[] { requirement }, user, resource: null);
        await new PermissionHandler().HandleAsync(context);
        return context;
    }

    // Test 5 — customer tokens (and tokens with no token_use) never satisfy a permission policy,
    // but must not hard-Fail either (default-deny is silent so composite policies still work).
    [Fact]
    public async Task Customer_and_missing_token_use_neither_succeed_nor_fail()
    {
        var customer = await Evaluate(
            RbacTestSupport.Principal(tokenUse: "customer", permissions: "wallet.adjust"), "wallet.adjust");
        Assert.False(customer.HasSucceeded);
        Assert.False(customer.HasFailed);

        var noTokenUse = await Evaluate(
            RbacTestSupport.Principal(permissions: "wallet.adjust"), "wallet.adjust");
        Assert.False(noTokenUse.HasSucceeded);
        Assert.False(noTokenUse.HasFailed);
    }

    // Test 6 — a held, normal-risk code succeeds; and platform_admin succeeds WITHOUT any
    // permissions claim (membership bypass) for a normal-risk code.
    [Fact]
    public async Task Held_normal_risk_code_and_platform_admin_succeed()
    {
        var staff = await Evaluate(
            RbacTestSupport.Principal(tokenUse: "user", permissions: "orders.create"), "orders.create");
        Assert.True(staff.HasSucceeded);
        Assert.False(staff.HasFailed);

        var admin = await Evaluate(
            RbacTestSupport.Principal(tokenUse: "user", userType: UserType.PlatformAdmin), "orders.create");
        Assert.True(admin.HasSucceeded);
        Assert.False(admin.HasFailed);
    }

    // Test 7 — held high/critical code without a fresh proof → typed step-up Fail carrying the code.
    [Fact]
    public async Task Held_high_risk_code_without_fresh_proof_fails_with_step_up_reason()
    {
        var context = await Evaluate(
            RbacTestSupport.Principal(
                tokenUse: "user",
                permissions: "wallet.adjust",
                stepUpPerms: "wallet.adjust"),   // no stepup_at → not fresh
            "wallet.adjust");

        Assert.True(context.HasFailed);
        Assert.False(context.HasSucceeded);

        var reason = Assert.Single(context.FailureReasons.OfType<StepUpRequiredFailureReason>());
        Assert.Equal("wallet.adjust", reason.PermissionCode);
    }

    // Test 7 (cont.) — a plain miss (code not held, not platform_admin) is a SILENT deny.
    [Fact]
    public async Task Plain_miss_neither_succeeds_nor_fails()
    {
        var context = await Evaluate(
            RbacTestSupport.Principal(
                tokenUse: "user",
                permissions: "orders.read",       // does not hold the required code
                stepUpPerms: "wallet.adjust"),
            "wallet.adjust");

        Assert.False(context.HasSucceeded);
        Assert.False(context.HasFailed);
    }
}
