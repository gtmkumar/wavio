namespace WaIntel.Application.Quality.Dtos;

/// <summary>API return shape for the tier-growth advisor (spec §4.6). See
/// <c>WaIntel.Application.Quality.Logic.TierRules.ComputeSafeDailySendPlan</c> for the
/// documented v1 heuristic behind these numbers.</summary>
public sealed record TierAdvisorDto(
    Guid PhoneNumberId,
    string CurrentTier,
    string? NextTier,
    long? CurrentDailyLimit,
    long RecentAverageDailyVolume,
    long RecommendedDailyVolume,
    bool ReadyToGrow,
    string Recommendation);
