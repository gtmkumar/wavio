using System.Security.Claims;
using wavio.Utilities.Auth;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace operations.Tests.Auth;

/// <summary>
/// Locks in the OR-policy handler (<see cref="AnyPermissionHandler"/>): it only demands step-up when
/// EVERY held alternative is high/critical; if any held alternative is normal-risk the policy is
/// satisfiable without step-up. Also guards the empty-requirement guard rail.
/// </summary>
public class AnyPermissionHandlerTests
{
    private static async Task<AuthorizationHandlerContext> Evaluate(
        ClaimsPrincipal user, params string[] alternatives)
    {
        var requirement = new AnyPermissionRequirement(alternatives);
        var context = new AuthorizationHandlerContext(new[] { requirement }, user, resource: null);
        await new AnyPermissionHandler().HandleAsync(context);
        return context;
    }

    // Test 8 — both held alternatives are high/critical and no fresh proof → step-up Fail.
    [Fact]
    public async Task All_held_alternatives_high_risk_without_proof_fails_with_step_up_reason()
    {
        var context = await Evaluate(
            RbacTestSupport.Principal(
                tokenUse: "user",
                permissions: "wallet.adjust orders.refund",
                stepUpPerms: "wallet.adjust orders.refund"), // no stepup_at → not fresh
            "wallet.adjust", "orders.refund");

        Assert.True(context.HasFailed);
        Assert.False(context.HasSucceeded);
        var reason = Assert.Single(context.FailureReasons.OfType<StepUpRequiredFailureReason>());
        Assert.Equal("wallet.adjust", reason.PermissionCode); // held.First()
    }

    // Test 8 (cont.) — one held alternative is normal-risk → policy satisfiable without step-up → Succeed.
    [Fact]
    public async Task One_held_normal_risk_alternative_succeeds_without_step_up()
    {
        var context = await Evaluate(
            RbacTestSupport.Principal(
                tokenUse: "user",
                permissions: "wallet.adjust orders.read",
                stepUpPerms: "wallet.adjust"),               // orders.read is NOT step-up-gated
            "wallet.adjust", "orders.read");

        Assert.True(context.HasSucceeded);
        Assert.False(context.HasFailed);
    }

    // Test 8 (cont.) — an empty alternatives set is a misconfiguration and is rejected at construction.
    [Fact]
    public void Empty_requirement_throws_argument_exception()
        => Assert.Throws<ArgumentException>(() => new AnyPermissionRequirement(Array.Empty<string>()));
}
