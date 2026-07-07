using WaAdmin.Application.Onboarding.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaAdmin.Application.Onboarding.Commands.RegisterPhoneNumber;

/// <summary>POST /v1/onboarding/phone-numbers/{id}/register — Cloud API registration with the
/// two-step verification pin. Returns null when the phone id is not in the caller's tenant
/// (endpoint answers 404).</summary>
public sealed record RegisterPhoneNumberCommand(Guid PhoneNumberId, string Pin, Guid TenantId, Guid? ActorId)
    : ICommand<OnboardingPhoneDto?>;
