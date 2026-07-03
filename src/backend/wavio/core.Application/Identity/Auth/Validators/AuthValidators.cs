using core.Application.Identity.Auth.Commands.Logout;
using core.Application.Identity.Auth.Commands.RefreshToken;
using core.Application.Identity.Auth.Dtos;
using FluentValidation;
using wavio.SharedDataModel.Enums;

namespace core.Application.Identity.Auth.Validators;

// NOTE on targeting (TARGET convention vs SOURCE):
//   In SOURCE these validators targeted the COMMAND and ran via a MediatR
//   ValidationPipelineBehavior after the endpoint resolved ip/ua.
//   In TARGET, validation runs as an endpoint filter (ValidationFilter<T>) against
//   the REQUEST DTO bound by the route — so the validators that gate user-supplied
//   fields are retargeted to the request records. The two refresh/logout validators
//   stay on the command because their request bodies are nullable-by-design
//   (body-wins-then-cookie fallback resolved in the endpoint); the empty-token guard
//   is enforced in their handlers (an empty/unknown token fails the hash lookup → 401).

public sealed class PasswordLoginValidator : AbstractValidator<PasswordLoginRequest>
{
    // Input-bound minimum: aligned to the password-SET policy (ResetPasswordValidator,
    // UserCommands CreateUser/InviteAccept) so that credentials that could never have
    // been stored via this system are rejected at the gate. Any legacy password shorter
    // than 8 chars or missing uppercase/digit could not have been created through the
    // standard flows; a legitimate user with such a password would already be unable
    // to reset it. Seeded dev admin is "Admin@123" (9 chars, 1 upper, 1 digit) — safe.
    public PasswordLoginValidator()
    {
        RuleFor(x => x.Identifier).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8)
            .MaximumLength(200)
            .Matches(@"[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
            .Matches(@"[0-9]").WithMessage("Password must contain at least one digit.");
    }
}

public sealed class OtpSendValidator : AbstractValidator<OtpSendRequest>
{
    private static readonly string[] ValidTypes    = ["phone", "email"];
    private static readonly string[] ValidPurposes = [
        OtpPurpose.Login, OtpPurpose.Signup, OtpPurpose.VerifyPhone,
        OtpPurpose.VerifyEmail, OtpPurpose.ResetPassword,
        // Step-up (§8): allow requesting a fresh OTP for a high/critical re-verification.
        OtpPurpose.SensitiveAction
    ];

    public OtpSendValidator()
    {
        RuleFor(x => x.Identifier).NotEmpty().MaximumLength(255);
        RuleFor(x => x.IdentifierType).NotEmpty().Must(t => ValidTypes.Contains(t))
            .WithMessage("identifierType must be 'phone' or 'email'.");
        RuleFor(x => x.Purpose).NotEmpty().Must(p => ValidPurposes.Contains(p))
            .WithMessage("Invalid OTP purpose.");
    }
}

public sealed class OtpVerifyValidator : AbstractValidator<OtpVerifyRequest>
{
    private static readonly string[] ValidTypes = ["phone", "email"];

    public OtpVerifyValidator()
    {
        RuleFor(x => x.Identifier).NotEmpty().MaximumLength(255);
        RuleFor(x => x.IdentifierType).NotEmpty().Must(t => ValidTypes.Contains(t));
        RuleFor(x => x.Purpose).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().Length(6).Matches(@"^\d{6}$")
            .WithMessage("OTP must be exactly 6 digits.");
    }
}

public sealed class StepUpVerifyValidator : AbstractValidator<StepUpVerifyRequest>
{
    private static readonly string[] ValidTypes = ["phone", "email"];

    public StepUpVerifyValidator()
    {
        RuleFor(x => x.IdentifierType).NotEmpty().Must(t => ValidTypes.Contains(t))
            .WithMessage("identifierType must be 'phone' or 'email'.");
        RuleFor(x => x.Code).NotEmpty().Length(6).Matches(@"^\d{6}$")
            .WithMessage("OTP must be exactly 6 digits.");
    }
}

public sealed class RefreshTokenValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenValidator()
    {
        RuleFor(x => x.RawRefreshToken).NotEmpty();
    }
}

public sealed class LogoutValidator : AbstractValidator<LogoutCommand>
{
    public LogoutValidator()
    {
        RuleFor(x => x.RawRefreshToken).NotEmpty();
    }
}

public sealed class ForgotPasswordValidator : AbstractValidator<ForgotPasswordRequest>
{
    private static readonly string[] ValidTypes = ["phone", "email"];

    public ForgotPasswordValidator()
    {
        RuleFor(x => x.Identifier).NotEmpty().MaximumLength(255);
        RuleFor(x => x.IdentifierType).NotEmpty().Must(t => ValidTypes.Contains(t));
    }
}

public sealed class ResetPasswordValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .MinimumLength(8)
            .MaximumLength(200)
            .Matches(@"[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
            .Matches(@"[0-9]").WithMessage("Password must contain at least one digit.");
    }
}
