namespace WaBilling.Application.RateCards.Dtos;

public static class RateCardCategories
{
    public const string Marketing = "marketing";
    public const string Utility = "utility";
    public const string Authentication = "authentication";
    public const string AuthenticationInternational = "authentication_international";
    public const string Service = "service";

    public static readonly IReadOnlyList<string> All =
    [
        Marketing, Utility, Authentication, AuthenticationInternational, Service
    ];
}

public sealed record RateCardEntryDto(
    Guid Id,
    string Category,
    string Market,
    string? VolumeTier,
    decimal PricePerMessage,
    string Currency);

public sealed record RateCardDto(
    Guid Id,
    string Name,
    string Currency,
    string Source,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string Status,
    string? Notes,
    IReadOnlyList<RateCardEntryDto> Entries);

/// <summary>POST /v1/rate-cards body — full upsert: creates a new card when
/// <c>RateCardId</c> route/query value is absent, otherwise replaces the target card's header
/// fields and its entire entry set (simplest correct "upsert" semantics for a small, admin-edited
/// table — no per-entry diffing).</summary>
public sealed record UpsertRateCardEntryRequest(
    string Category,
    string Market,
    string? VolumeTier,
    decimal PricePerMessage);

public sealed record UpsertRateCardRequest(
    string Name,
    string Currency,
    string Source,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string Status,
    string? Notes,
    List<UpsertRateCardEntryRequest> Entries);
