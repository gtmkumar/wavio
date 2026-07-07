using WaAdmin.Application.Onboarding.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaAdmin.Application.Onboarding.Commands.CompleteEmbeddedSignup;

/// <summary>POST /v1/onboarding/embedded-signup (spec §7.1) — the server side of the Embedded
/// Signup popup: exchange the code for a business token, discover the granted WABA, persist
/// WABA + phone numbers (token envelope-encrypted), and subscribe webhooks. Idempotent per
/// WABA: re-running upserts by meta ids instead of duplicating rows.</summary>
public sealed record CompleteEmbeddedSignupCommand(EmbeddedSignupRequest Request, Guid TenantId, Guid? ActorId)
    : ICommand<OnboardingStatusDto>;
