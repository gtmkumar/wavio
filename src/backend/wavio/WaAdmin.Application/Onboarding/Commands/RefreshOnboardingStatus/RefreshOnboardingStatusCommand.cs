using WaAdmin.Application.Onboarding.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaAdmin.Application.Onboarding.Commands.RefreshOnboardingStatus;

/// <summary>POST /v1/onboarding/refresh — re-pulls review/quality statuses from the Graph API
/// and mirrors them into the waba tables, then returns the fresh snapshot. A command (it writes),
/// but a benign one: it only mirrors Meta-owned state, so the endpoint gates it with the read
/// permission — the wizard polls it while waiting on Meta reviews.</summary>
public sealed record RefreshOnboardingStatusCommand(Guid TenantId, Guid? ActorId)
    : ICommand<OnboardingStatusDto>;
