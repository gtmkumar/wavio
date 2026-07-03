using Microsoft.AspNetCore.Authorization;

namespace wavio.Utilities.Auth;

/// <summary>
/// Marks a policy denial that happened ONLY because a fresh step-up (§8) is missing for a
/// high/critical permission — as distinct from a plain "not granted" denial. Attached via
/// <see cref="AuthorizationHandlerContext.Fail(AuthorizationFailureReason)"/> by the permission
/// handlers; <see cref="StepUpAuthorizationResultHandler"/> detects it and emits a structured
/// <c>403 step_up_required</c> carrying <see cref="PermissionCode"/> so the client can prompt an
/// OTP, call /auth/step-up/verify, swap its access token, and retry.
/// </summary>
public sealed class StepUpRequiredFailureReason : AuthorizationFailureReason
{
    public string PermissionCode { get; }

    public StepUpRequiredFailureReason(IAuthorizationHandler handler, string permissionCode)
        : base(handler, $"step_up_required:{permissionCode}")
        => PermissionCode = permissionCode;
}
