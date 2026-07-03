using System.Text.Json;
using wavio.Utilities.ApiResponse.ResponseUtil;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace wavio.Utilities.Auth;

/// <summary>
/// Turns a step-up policy denial (§8) into a structured <c>403 step_up_required</c> so the client can
/// prompt an OTP, call /auth/step-up/verify, swap its access token, and retry. Without this there is no
/// <see cref="IAuthorizationMiddlewareResultHandler"/> in the app, so a failed policy is a bare empty
/// 403 with no machine-readable code. Only intercepts denials carrying a
/// <see cref="StepUpRequiredFailureReason"/>; every other result delegates to the framework default.
/// The body mirrors the <c>ExceptionHandler</c> envelope (Response/Message). Register once per host.
/// </summary>
public sealed class StepUpAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        var stepUp = authorizeResult.AuthorizationFailure?.FailureReasons
            .OfType<StepUpRequiredFailureReason>()
            .FirstOrDefault();

        if (authorizeResult.Forbidden && stepUp is not null && !context.Response.HasStarted)
        {
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";

            var payload = new Response
            {
                Status = false,
                Message = new Message
                {
                    ErrorTypeCode   = ErrorMessageEnum.Forbidden,
                    ResponseMessage = "step_up_required",
                    ErrorMessage    = new Dictionary<string, string[]>
                    {
                        ["step_up_required"] = [stepUp.PermissionCode],
                    },
                },
            };

            await JsonSerializer.SerializeAsync(context.Response.Body, payload, JsonOptions);
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
