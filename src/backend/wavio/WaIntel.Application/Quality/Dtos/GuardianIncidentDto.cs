namespace WaIntel.Application.Quality.Dtos;

/// <summary>API/return shape for a Guardian incident (issue #20, spec §4.6).</summary>
public sealed record GuardianIncidentDto(
    Guid Id,
    Guid PhoneNumberId,
    string IncidentType,
    string Severity,
    string Status,
    string ThrottleAction,
    string? TriggerRating,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ResolvedAt);
