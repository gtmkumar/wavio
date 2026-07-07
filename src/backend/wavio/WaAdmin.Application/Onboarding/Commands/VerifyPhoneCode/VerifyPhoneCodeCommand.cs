using WaAdmin.Application.Onboarding.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaAdmin.Application.Onboarding.Commands.VerifyPhoneCode;

/// <summary>POST /v1/onboarding/phone-numbers/{id}/verify-code — submits the OTP the business
/// received, proving number ownership. Returns null when the phone id is not in the caller's
/// tenant (endpoint answers 404).</summary>
public sealed record VerifyPhoneCodeCommand(Guid PhoneNumberId, string Code, Guid TenantId, Guid? ActorId)
    : ICommand<OnboardingPhoneDto?>;
