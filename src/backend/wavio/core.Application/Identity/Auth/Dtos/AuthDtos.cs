namespace core.Application.Identity.Auth.Dtos;

// ─── Request DTOs ──────────────────────────────────────────────────────────

public sealed record PasswordLoginRequest(string Identifier, string Password);

public sealed record OtpSendRequest(string Identifier, string IdentifierType, string Purpose);

public sealed record OtpVerifyRequest(string Identifier, string IdentifierType, string Purpose, string Code);

public sealed record RefreshTokenRequest(string RefreshToken);

// System-user refresh body: RefreshToken is nullable so admin-web can omit it and
// supply the token via the HttpOnly `lg_refresh` cookie instead. The endpoint resolves
// body-first, cookie-fallback before building the command. pos-web / mobile system users
// keep sending the token in the body (body wins for backward compat). Kept separate from
// the shared RefreshTokenRequest so the customer lane's non-null contract is untouched.
public sealed record SystemRefreshTokenRequest(string? RefreshToken = null);

public sealed record LogoutRequest(string RefreshToken);

// System-user logout body: RefreshToken is optional so admin-web can log out after a
// hard reload (when it no longer holds the token in memory and the HttpOnly cookie is
// path-scoped to /refresh, hence not sent to /logout). The endpoint always clears the
// cookie; it only revokes the token family when a token is actually supplied. Separate
// from the shared LogoutRequest so the customer lane's non-null contract is untouched.
public sealed record SystemLogoutRequest(string? RefreshToken = null);

public sealed record ForgotPasswordRequest(string Identifier, string IdentifierType);

public sealed record ResetPasswordRequest(string Token, string NewPassword);

// ─── Response DTOs ─────────────────────────────────────────────────────────

public sealed record TokenResponse(string AccessToken, string RefreshToken, int ExpiresInSeconds, string TokenType = "Bearer");

public sealed record OtpSentResponse(string Message, DateTimeOffset ExpiresAt);

public sealed record OtpVerifiedResponse(string AccessToken, string RefreshToken, int ExpiresInSeconds, string TokenType = "Bearer");

/// <summary>Step-up (§8) verify request. The OTP was sent to the caller's own phone/email
/// (purpose=sensitive_action); the identifier is derived server-side from the authenticated token.</summary>
public sealed record StepUpVerifyRequest(string IdentifierType, string Code);

/// <summary>Result of a successful step-up: an UPGRADED access token carrying amr+stepup_at.
/// No refresh token is issued (this is not a login) — the client swaps its in-memory access token
/// and retries the high/critical action.</summary>
public sealed record StepUpVerifiedResponse(string AccessToken, int ExpiresInSeconds, string TokenType = "Bearer");
