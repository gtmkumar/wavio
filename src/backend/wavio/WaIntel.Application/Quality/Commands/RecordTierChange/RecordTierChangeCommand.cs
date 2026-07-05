using WaIntel.Application.Quality.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaIntel.Application.Quality.Commands.RecordTierChange;

/// <summary>
/// Records a phone number's messaging-tier change (issue #20, spec §4.2/§4.6) and opens a
/// <c>tier_downgrade</c> incident when the new tier ranks below the currently stored one.
/// <paramref name="RawNewTier"/>/<paramref name="RawOldTier"/> are Meta's raw tier codes (e.g.
/// TIER_1K) — canonicalized internally via <c>QualityCodes.TryNormalizeTier</c>.
/// </summary>
public sealed record RecordTierChangeCommand(
    Guid TenantId,
    Guid PhoneNumberId,
    string WabaId,
    string? RawOldTier,
    string RawNewTier,
    string EventSource,
    string? RawPayload) : ICommand<GuardianIncidentDto?>;
