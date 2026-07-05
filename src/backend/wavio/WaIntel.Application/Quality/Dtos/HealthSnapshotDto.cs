namespace WaIntel.Application.Quality.Dtos;

/// <summary>API/return shape for a weekly per-number health snapshot (issue #20, spec §4.6).</summary>
public sealed record HealthSnapshotDto(
    Guid PhoneNumberId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal? DeliveryRate,
    decimal? ReadRate,
    decimal? BlockProxyRate,
    string? QualityRating,
    string? MessagingTier,
    long? TierHeadroom,
    long MessagesSent,
    long MessagesDelivered,
    long MessagesRead,
    long MessagesFailed);

/// <summary>The tenant-facing weekly health report (spec §4.6): each number's latest snapshot
/// alongside any currently open Guardian incidents affecting it.</summary>
public sealed record HealthReportDto(
    IReadOnlyList<HealthSnapshotDto> Snapshots,
    IReadOnlyList<GuardianIncidentDto> OpenIncidents);
