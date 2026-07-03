using core.Application.Identity.Auth.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace core.Application.Identity.Auth.Commands.StepUpVerify;

/// <summary>Verifies a fresh OTP (purpose=sensitive_action) for the AUTHENTICATED caller and
/// re-issues their access token with amr+stepup_at so high/critical actions pass the §8 step-up
/// gate for a short window. Distinct from OtpVerifyCommand (login): no refresh token, no new session,
/// identifier derived from the caller's own token — a user can only step up their own account.</summary>
public sealed record StepUpVerifyCommand(StepUpVerifyRequest Request) : ICommand<StepUpVerifiedResponse>;
