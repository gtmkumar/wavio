using Wavio.Utilities.CQRS.Abstractions;

namespace WaAdmin.Application.Onboarding.Commands.RequestVerificationCode;

/// <summary>POST /v1/onboarding/phone-numbers/{id}/request-code — asks Meta to send the OTP
/// (SMS or VOICE) that proves number ownership. Returns false when the phone id is not in the
/// caller's tenant (endpoint answers 404).</summary>
public sealed record RequestVerificationCodeCommand(
    Guid PhoneNumberId, string? CodeMethod, string? Language, Guid TenantId)
    : ICommand<bool>;
