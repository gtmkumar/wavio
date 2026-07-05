using WaIntel.Application.Quality.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaIntel.Application.Quality.Commands.RecordQualityChange;

/// <summary>
/// Records a phone number's quality-rating change and applies Guardian's auto-throttle policy
/// (issue #20, spec §4.6). <paramref name="RawNewRating"/> is whatever Meta's webhook (or the
/// simulate endpoint) sent — normalized internally via
/// <c>WaIntel.Application.Quality.Logic.QualityCodes</c>. Returns the incident opened/resolved by
/// this change, or null if the rating didn't actually change or no incident applies.
/// </summary>
public sealed record RecordQualityChangeCommand(
    Guid TenantId,
    Guid PhoneNumberId,
    string WabaId,
    string RawNewRating,
    string EventSource,
    string? RawPayload) : ICommand<GuardianIncidentDto?>;
