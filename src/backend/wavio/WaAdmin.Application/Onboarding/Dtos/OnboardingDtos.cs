namespace WaAdmin.Application.Onboarding.Dtos;

// Wire DTOs for /v1/onboarding (docs/ONBOARDING_WIZARD_PLAN.md). The status DTO is the wizard's
// single source of resumability: every step derives "where am I" from it, nothing stores a
// wizard cursor.

/// <summary>Relayed from the Embedded Signup popup: the authorization code, plus the popup's
/// sessionInfo ids when available (stub mode sends only a simulated code — the backend then
/// discovers the WABA via debug_token).</summary>
public sealed record EmbeddedSignupRequest(string Code, string? WabaId, string? PhoneNumberId);

public sealed record RegisterPhoneRequest(string Pin);

public sealed record RequestCodeRequest(string? CodeMethod, string? Language);

public sealed record VerifyCodeRequest(string Code);

public sealed record UpdateBusinessProfileRequest(
    string? About,
    string? Address,
    string? Description,
    string? Email,
    string[]? Websites,
    string? Vertical,
    string? ProfilePictureUrl);

public sealed record BusinessProfileDto(
    string? About,
    string? Address,
    string? Description,
    string? Email,
    string[] Websites,
    string? Vertical,
    string? ProfilePictureUrl);

public sealed record OnboardingStatusDto(
    bool Connected,
    OnboardingBusinessAccountDto? BusinessAccount,
    IReadOnlyList<OnboardingPhoneDto> PhoneNumbers,
    IReadOnlyList<OnboardingCheckDto> Checks);

/// <summary>Never carries the token itself — only whether one is stored.</summary>
public sealed record OnboardingBusinessAccountDto(
    Guid Id,
    string MetaWabaId,
    string Name,
    string? CurrencyCode,
    string? VerificationStatus,
    DateTimeOffset? WebhooksSubscribedAt,
    bool HasToken);

public sealed record OnboardingPhoneDto(
    Guid Id,
    string MetaPhoneNumberId,
    string DisplayPhoneNumber,
    string? VerifiedName,
    string Status,
    string? CodeVerificationStatus,
    string? NameStatus,
    string? QualityRating,
    string? MessagingTier,
    DateTimeOffset? RegisteredAt,
    bool ProfileSet);

/// <summary>One row of the step-4 checklist. <see cref="State"/> is one of
/// <c>done</c> / <c>waiting</c> (Meta review in progress — nothing for the user to do) /
/// <c>todo</c> (user action needed) / <c>attention</c> (something Meta flagged).
/// <see cref="Detail"/> carries the raw Meta value for the console to explain.</summary>
public sealed record OnboardingCheckDto(string Key, string State, string? Detail);
