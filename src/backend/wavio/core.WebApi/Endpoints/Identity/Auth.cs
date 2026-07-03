using core.Application.Common;
using core.Application.Identity.Auth.Commands.AcceptInvite;
using core.Application.Identity.Auth.Commands.ForgotPassword;
using core.Application.Identity.Auth.Commands.Logout;
using core.Application.Identity.Auth.Commands.OtpSend;
using core.Application.Identity.Auth.Commands.OtpVerify;
using core.Application.Identity.Auth.Commands.StepUpVerify;
using core.Application.Identity.Auth.Commands.PasswordLogin;
using core.Application.Identity.Auth.Commands.RefreshToken;
using core.Application.Identity.Auth.Commands.ResetPassword;
using core.Application.Identity.Auth.Dtos;
using core.Application.Identity.Auth.Queries.GetInvitePreview;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.Utilities.ApiResponse.ResponseUtil;
using wavio.Utilities.Endpoints;
using wavio.Utilities.Validation;
using Microsoft.Extensions.Options;

namespace core.WebApi.Endpoints.Identity;

/// <summary>
/// POST /api/v1/auth/password/login
/// POST /api/v1/auth/otp/send
/// POST /api/v1/auth/otp/verify
/// POST /api/v1/auth/refresh
/// POST /api/v1/auth/logout
/// POST /api/v1/auth/password/forgot
/// POST /api/v1/auth/password/reset
/// GET  /api/v1/auth/invite/{token}
/// POST /api/v1/auth/accept-invite
/// </summary>
public class Auth : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/auth";

    // Name of the HttpOnly refresh-token cookie for system users (admin-web).
    private const string RefreshCookieName = "lg_refresh";

    // Path the cookie is scoped to. The browser only sends `lg_refresh` to the
    // refresh endpoint, never to any other route — minimizing exposure surface.
    //
    // IMPORTANT: admin-web reaches Identity through the gateway, which serves it
    // under the "/identity" prefix (VITE_IDENTITY_URL = http://host:8080/identity)
    // and strips that prefix before forwarding here. A cookie Path is matched by
    // the browser against the *outgoing request URL*, which still carries the
    // "/identity" prefix — so the Path MUST include it, otherwise the browser
    // never sends the cookie back and cookie-backed silent refresh (after a hard
    // reload, when the in-memory refresh token is gone) always 401s.
    private const string RefreshCookiePath = "/identity/api/v1/auth/refresh";

    public static void Map(RouteGroupBuilder group)
    {
        // C5: rate-limit the entire auth group (10 req / 60 s per client IP)
        group.WithTags("Auth").RequireRateLimiting("auth");

        // POST /api/v1/auth/password/login
        group.MapPost("/password/login", async (
            PasswordLoginRequest req,
            HttpContext ctx,
            IDispatcher dispatcher,
            IOptions<JwtSettings> jwt,
            IWebHostEnvironment env,
            CancellationToken ct) =>
        {
            var ip = ctx.Connection.RemoteIpAddress?.ToString();
            var ua = ctx.Request.Headers.UserAgent.ToString();
            var result = await dispatcher.SendAsync(new PasswordLoginCommand(req.Identifier, req.Password, ip, ua), ct);
            // Also set the refresh token as an HttpOnly cookie for browser system users (admin-web).
            // The body still carries it for pos-web / mobile / scripts (backward compat).
            SetRefreshCookie(ctx, result.RefreshToken, jwt.Value.RefreshDays, env.IsDevelopment());
            return Results.Ok(new SingleResponse<TokenResponse> { Status = true, Data = result });
        })
        .AddEndpointFilter<ValidationFilter<PasswordLoginRequest>>()
        .WithName("PasswordLogin")
        .Produces<SingleResponse<TokenResponse>>()
        .ProducesProblem(401)
        .AllowAnonymous();

        // POST /api/v1/auth/otp/send
        group.MapPost("/otp/send", async (
            OtpSendRequest req,
            HttpContext ctx,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var ip = ctx.Connection.RemoteIpAddress?.ToString();
            var ua = ctx.Request.Headers.UserAgent.ToString();
            var result = await dispatcher.SendAsync(new OtpSendCommand(req.Identifier, req.IdentifierType, req.Purpose, ip, ua), ct);
            return Results.Ok(new SingleResponse<OtpSentResponse> { Status = true, Data = result });
        })
        .AddEndpointFilter<ValidationFilter<OtpSendRequest>>()
        .WithName("OtpSend")
        .Produces<SingleResponse<OtpSentResponse>>()
        .AllowAnonymous();

        // POST /api/v1/auth/otp/verify
        group.MapPost("/otp/verify", async (
            OtpVerifyRequest req,
            HttpContext ctx,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var ip = ctx.Connection.RemoteIpAddress?.ToString();
            var ua = ctx.Request.Headers.UserAgent.ToString();
            var result = await dispatcher.SendAsync(new OtpVerifyCommand(req.Identifier, req.IdentifierType, req.Purpose, req.Code, ip, ua), ct);
            return Results.Ok(new SingleResponse<OtpVerifiedResponse> { Status = true, Data = result });
        })
        .AddEndpointFilter<ValidationFilter<OtpVerifyRequest>>()
        .WithName("OtpVerify")
        .Produces<SingleResponse<OtpVerifiedResponse>>()
        .AllowAnonymous();

        // POST /api/v1/auth/step-up/verify — §8: fresh OTP re-verification for a high/critical action.
        // Authenticated (unlike /otp/verify): the identifier is derived from the caller's own token,
        // and the response is an UPGRADED access token carrying amr+stepup_at (no refresh token).
        // Send the OTP first via /otp/send with purpose=sensitive_action.
        group.MapPost("/step-up/verify", async (
            StepUpVerifyRequest req,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(new StepUpVerifyCommand(req), ct);
            return Results.Ok(new SingleResponse<StepUpVerifiedResponse> { Status = true, Data = result });
        })
        .AddEndpointFilter<ValidationFilter<StepUpVerifyRequest>>()
        .WithName("StepUpVerify")
        .Produces<SingleResponse<StepUpVerifiedResponse>>()
        .RequireAuthorization();

        // POST /api/v1/auth/refresh
        group.MapPost("/refresh", async (
            SystemRefreshTokenRequest req,
            HttpContext ctx,
            IDispatcher dispatcher,
            IOptions<JwtSettings> jwt,
            IWebHostEnvironment env,
            CancellationToken ct) =>
        {
            var ip = ctx.Connection.RemoteIpAddress?.ToString();
            var ua = ctx.Request.Headers.UserAgent.ToString();

            // Body wins (pos-web / mobile system users); fall back to the HttpOnly
            // cookie (admin-web). If neither is present the handler rejects the empty
            // token with a 401, which the client treats as a failed refresh.
            var rawRefresh = !string.IsNullOrWhiteSpace(req.RefreshToken)
                ? req.RefreshToken!
                : ctx.Request.Cookies[RefreshCookieName] ?? string.Empty;

            var result = await dispatcher.SendAsync(new RefreshTokenCommand(rawRefresh, ip, ua), ct);
            // Rotate the cookie with the new refresh token (refresh-token rotation).
            SetRefreshCookie(ctx, result.RefreshToken, jwt.Value.RefreshDays, env.IsDevelopment());
            return Results.Ok(new SingleResponse<TokenResponse> { Status = true, Data = result });
        })
        .WithName("RefreshToken")
        .Produces<SingleResponse<TokenResponse>>()
        .AllowAnonymous();

        // POST /api/v1/auth/logout
        group.MapPost("/logout", async (
            SystemLogoutRequest req,
            HttpContext ctx,
            IDispatcher dispatcher,
            IWebHostEnvironment env,
            CancellationToken ct) =>
        {
            // Body wins; fall back to the cookie (only reachable if a future cookie scope
            // includes /logout). pos-web / mobile send the token in the body; admin-web
            // sends its in-memory token when it still has one (fresh login), and nothing
            // after a hard reload — in which case we just clear the cookie below.
            var rawRefresh = !string.IsNullOrWhiteSpace(req.RefreshToken)
                ? req.RefreshToken!
                : ctx.Request.Cookies[RefreshCookieName];

            // Revoke the token family only when we actually have a token. The handler
            // requires a non-empty token, so skip the command entirely when there is none.
            if (!string.IsNullOrWhiteSpace(rawRefresh))
                await dispatcher.SendAsync(new LogoutCommand(rawRefresh), ct);

            // Always clear the HttpOnly cookie on logout. Append with Max-Age=0 deletes it
            // by name+path even though the cookie is scoped to the refresh path (not /logout).
            ClearRefreshCookie(ctx, env.IsDevelopment());
            return Results.Ok(new Response { Status = true, Message = new Message { ResponseMessage = "Logged out successfully." } });
        })
        .WithName("Logout")
        .RequireAuthorization();

        // POST /api/v1/auth/password/forgot
        group.MapPost("/password/forgot", async (
            ForgotPasswordRequest req,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            await dispatcher.SendAsync(new ForgotPasswordCommand(req.Identifier, req.IdentifierType), ct);
            return Results.Ok(new Response { Status = true, Message = new Message { ResponseMessage = "If an account exists, a reset link has been sent." } });
        })
        .AddEndpointFilter<ValidationFilter<ForgotPasswordRequest>>()
        .WithName("ForgotPassword")
        .AllowAnonymous();

        // POST /api/v1/auth/password/reset
        group.MapPost("/password/reset", async (
            ResetPasswordRequest req,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            await dispatcher.SendAsync(new ResetPasswordCommand(req.Token, req.NewPassword), ct);
            return Results.Ok(new Response { Status = true, Message = new Message { ResponseMessage = "Password reset successfully." } });
        })
        .AddEndpointFilter<ValidationFilter<ResetPasswordRequest>>()
        .WithName("ResetPassword")
        .AllowAnonymous();

        // GET /api/v1/auth/invite/{token} — validate an invitation, return who it's for
        group.MapGet("/invite/{token}", async (string token, IDispatcher dispatcher, CancellationToken ct) =>
        {
            var preview = await dispatcher.QueryAsync(new GetInvitePreviewQuery(token), ct);
            return Results.Ok(new SingleResponse<InvitePreviewDto> { Status = true, Data = preview });
        })
        .WithName("GetInvitePreview")
        .AllowAnonymous();

        // POST /api/v1/auth/accept-invite — set password + activate via invitation token
        group.MapPost("/accept-invite", async (AcceptInviteRequest req, IDispatcher dispatcher, CancellationToken ct) =>
        {
            await dispatcher.SendAsync(new AcceptInviteCommand(req), ct);
            return Results.Ok(new Response { Status = true, Message = new Message { ResponseMessage = "Your account is now active. You can sign in." } });
        })
        .WithName("AcceptInvite")
        .AllowAnonymous();
    }

    /// <summary>
    /// Writes the refresh token to the HttpOnly `lg_refresh` cookie.
    ///   HttpOnly      — never readable from JS (the XSS fix).
    ///   Secure        — only sent over HTTPS outside Development (http://localhost works in dev).
    ///   SameSite=Strict — not sent on cross-site navigations (CSRF hardening).
    ///   Path          — scoped to the refresh endpoint only.
    ///   Max-Age       — the refresh-token lifetime (Jwt:RefreshDays).
    /// admin-web stops persisting the refresh token in localStorage and relies on
    /// this cookie for silent refresh. The token is ALSO still returned in the body
    /// for pos-web / mobile system users (body wins on refresh for backward compat).
    /// </summary>
    private static void SetRefreshCookie(HttpContext ctx, string refreshToken, int refreshDays, bool isDev)
    {
        ctx.Response.Cookies.Append(RefreshCookieName, refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure   = !isDev,                 // dev omits Secure so http://localhost works
            SameSite = SameSiteMode.Strict,
            Path     = RefreshCookiePath,
            MaxAge   = TimeSpan.FromDays(refreshDays),
        });
    }

    /// <summary>Clears the refresh cookie. Path + attributes must match SetRefreshCookie.</summary>
    private static void ClearRefreshCookie(HttpContext ctx, bool isDev)
    {
        ctx.Response.Cookies.Append(RefreshCookieName, string.Empty, new CookieOptions
        {
            HttpOnly = true,
            Secure   = !isDev,
            SameSite = SameSiteMode.Strict,
            Path     = RefreshCookiePath,
            MaxAge   = TimeSpan.Zero,
        });
    }
}
