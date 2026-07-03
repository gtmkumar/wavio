using System.Security.Claims;
using wavio.SharedDataModel.Enums;
using Microsoft.AspNetCore.Authorization;

namespace wavio.Utilities.Auth;

/// <summary>
/// Evaluates PermissionRequirement by checking the "permissions" claim in the JWT.
/// Implicitly requires token_use=user — customer tokens carry no permissions and must
/// never satisfy admin permission policies even if they happen to be authenticated.
/// Platform admins (user_type=platform_admin) bypass the individual permission (membership)
/// check — but NOT the §8 step-up gate for high/critical actions (their token carries the full
/// high/critical catalog in step_up_perms), so critical actions still require a fresh re-verification.
/// </summary>
public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        // Gate 1: token_use must be "user". Customer tokens (token_use=customer) are
        // silently rejected here — they will not satisfy any permission-based policy.
        var tokenUse = context.User.FindFirstValue("token_use");
        if (!string.Equals(tokenUse, TokenClaims.TokenUseValue, StringComparison.Ordinal))
            return Task.CompletedTask; // leave requirement unsatisfied

        // Canonicalize so renamed permissions resolve across the rename (legacy claims in
        // already-issued JWTs still satisfy the new code). See PermissionAlias.
        var required = PermissionAlias.Canonical(requirement.PermissionCode);

        // Gate 2: membership. Platform admins bypass THIS check; everyone else must hold the code.
        var userType = context.User.FindFirstValue("user_type");
        bool granted = userType == UserType.PlatformAdmin;
        if (!granted)
        {
            var permsClaim = context.User.FindFirstValue("permissions");
            if (!string.IsNullOrEmpty(permsClaim))
                granted = permsClaim.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                    .Select(PermissionAlias.Canonical)
                                    .Contains(required, StringComparer.OrdinalIgnoreCase);
        }
        if (!granted)
            return Task.CompletedTask; // default-deny — never Fail on a plain miss (other handlers may satisfy)

        // Gate 3: step-up (§8). A high/critical action requires a fresh OTP re-verification even for
        // platform_admin. Fail with a typed reason so StepUpAuthorizationResultHandler emits a
        // structured 403 step_up_required (rather than a bare deny) and the client can prompt + retry.
        if (StepUp.RequiresStepUp(context.User, required) && !StepUp.IsFresh(context.User))
        {
            context.Fail(new StepUpRequiredFailureReason(this, required));
            return Task.CompletedTask;
        }

        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
