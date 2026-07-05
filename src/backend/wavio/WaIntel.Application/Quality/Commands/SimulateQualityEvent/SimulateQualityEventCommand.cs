using WaIntel.Application.Quality.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaIntel.Application.Quality.Commands.SimulateQualityEvent;

/// <summary>
/// QA/admin-only: injects a simulated quality-rating and/or messaging-tier change (issue #20
/// acceptance criterion — exercises Guardian without a real Meta webhook), mirroring the
/// <c>SimulateWindowCommand</c> idiom (issue #15). At least one of
/// <paramref name="SimulatedRating"/>/<paramref name="SimulatedTier"/> should be supplied; either
/// may be omitted to simulate only one dimension at a time.
/// NEVER reachable in Production — see <see cref="SimulateQualityEventHandler"/> and the WebApi
/// endpoint's environment gate + <c>quality.simulate</c> permission gate (defense in depth).
/// </summary>
public sealed record SimulateQualityEventCommand(
    Guid TenantId,
    Guid PhoneNumberId,
    string? SimulatedRating,
    string? SimulatedTier) : ICommand<GuardianIncidentDto?>;
