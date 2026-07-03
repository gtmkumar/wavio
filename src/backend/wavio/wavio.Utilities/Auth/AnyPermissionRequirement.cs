using System.Security.Claims;
using wavio.SharedDataModel.Enums;
using Microsoft.AspNetCore.Authorization;

namespace wavio.Utilities.Auth;

/// <summary>
/// Authorization requirement that succeeds when the caller holds ANY ONE of the listed
/// permission codes. Used by pipe-syntax policies (e.g. "permission:orders.create|pos.order.create")
/// to gate shared endpoints that two independent role families must reach.
///
/// Platform admins bypass this requirement exactly as they do PermissionRequirement.
/// </summary>
public sealed class AnyPermissionRequirement : IAuthorizationRequirement
{
    /// <summary>At least one of these codes must be present in the caller's permissions claim.</summary>
    public IReadOnlyList<string> PermissionCodes { get; }

    public AnyPermissionRequirement(IEnumerable<string> codes)
    {
        var list = codes.ToList();
        if (list.Count == 0) throw new ArgumentException("At least one permission code is required.", nameof(codes));
        PermissionCodes = list;
    }
}

/// <summary>
/// Evaluates <see cref="AnyPermissionRequirement"/> against the JWT permissions claim.
/// Mirrors the logic in <see cref="PermissionHandler"/> — token_use=user required,
/// platform_admin bypasses, any matching code in the space-separated claim satisfies.
/// </summary>
public sealed class AnyPermissionHandler : AuthorizationHandler<AnyPermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AnyPermissionRequirement requirement)
    {
        var tokenUse = context.User.FindFirstValue("token_use");
        if (!string.Equals(tokenUse, TokenClaims.TokenUseValue, StringComparison.Ordinal))
            return Task.CompletedTask; // leave unsatisfied

        // Which of the OR-alternatives can this caller actually satisfy the policy with?
        // Platform admins effectively hold all of them; everyone else holds those in their claim.
        var required = requirement.PermissionCodes.Select(PermissionAlias.Canonical).ToList();
        var userType = context.User.FindFirstValue("user_type");
        List<string> held;
        if (userType == UserType.PlatformAdmin)
        {
            held = required;
        }
        else
        {
            var permsClaim = context.User.FindFirstValue("permissions");
            var perms = string.IsNullOrEmpty(permsClaim)
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : permsClaim.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                            .Select(PermissionAlias.Canonical)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);
            held = required.Where(perms.Contains).ToList();
        }

        if (held.Count == 0)
            return Task.CompletedTask; // not granted any alternative — default-deny

        // Step-up (§8): only require it when EVERY alternative the caller could use is high/critical.
        // If any held alternative is low/normal risk, the policy is satisfiable without step-up, so we
        // must not block it. Reason carries the first risky code for the client's prompt.
        if (!StepUp.IsFresh(context.User) && held.All(c => StepUp.RequiresStepUp(context.User, c)))
        {
            context.Fail(new StepUpRequiredFailureReason(this, held.First()));
            return Task.CompletedTask;
        }

        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
