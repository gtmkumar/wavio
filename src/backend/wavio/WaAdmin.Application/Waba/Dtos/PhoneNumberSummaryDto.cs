namespace WaAdmin.Application.Waba.Dtos;

/// <summary>Read-only picker projection of waba.phone_numbers for the admin console (campaign
/// "send from" / template business-account selection). Deliberately excludes Meta-side ids and
/// audit columns — this is a UI lookup, not the WABA onboarding surface (issue #6/#14).</summary>
public sealed record PhoneNumberSummaryDto(
    Guid Id,
    Guid BusinessAccountId,
    string DisplayPhoneNumber,
    string Status,
    string? QualityRating,
    string? MessagingTier);
