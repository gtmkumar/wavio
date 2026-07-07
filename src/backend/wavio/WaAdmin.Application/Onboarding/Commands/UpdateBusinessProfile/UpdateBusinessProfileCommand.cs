using WaAdmin.Application.Onboarding.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaAdmin.Application.Onboarding.Commands.UpdateBusinessProfile;

/// <summary>PUT /v1/onboarding/phone-numbers/{id}/profile — writes the public WhatsApp business
/// profile to Meta first, then mirrors it into waba.business_profiles (1:1 per phone). Returns
/// null when the phone id is not in the caller's tenant (endpoint answers 404).</summary>
public sealed record UpdateBusinessProfileCommand(
    Guid PhoneNumberId, UpdateBusinessProfileRequest Request, Guid TenantId, Guid? ActorId)
    : ICommand<BusinessProfileDto?>;
