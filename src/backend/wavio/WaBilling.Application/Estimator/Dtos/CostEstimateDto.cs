namespace WaBilling.Application.Estimator.Dtos;

/// <summary>GET /v1/costs/estimate result. <see cref="Found"/> is false when no active rate card
/// (or no priced entry for this category/market) exists yet — callers should show "unpriced",
/// never a silently-wrong zero.</summary>
public sealed record CostEstimateDto(
    bool Found,
    bool Billable,
    decimal Amount,
    string Currency,
    string Category,
    string Market,
    string? VolumeTier,
    Guid? RateCardId,
    string Reason);
