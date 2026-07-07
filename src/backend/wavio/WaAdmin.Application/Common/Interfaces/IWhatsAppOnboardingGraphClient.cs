namespace WaAdmin.Application.Common.Interfaces;

/// <summary>
/// Graph API surface for the WhatsApp onboarding wizard (docs/ONBOARDING_WIZARD_PLAN.md, spec
/// §4.1): Embedded Signup token exchange, WABA/phone discovery, Cloud API registration, OTP
/// verification, business profile, webhook subscription, and review-status polling. Implemented
/// by <c>MetaGraphOnboardingClient</c> against <c>Meta:Graph:BaseUrl</c> — real Meta in
/// production, tools/MetaGraphApiStub in dev. Every method takes the per-WABA business token
/// explicitly (decrypted at the call site from
/// <c>waba.business_accounts.system_user_token_ciphertext</c>) — tokens are never cached here.
/// </summary>
public interface IWhatsAppOnboardingGraphClient
{
    /// <summary>Exchanges the Embedded Signup authorization code for a business token.</summary>
    Task<GraphTokenResult> ExchangeCodeAsync(string code, CancellationToken cancellationToken);

    /// <summary>Which WABA ids the token grants (debug_token granular_scopes) — how the backend
    /// discovers the onboarded WABA when the popup's sessionInfo was not relayed.</summary>
    Task<IReadOnlyList<string>> GetGrantedWabaIdsAsync(string accessToken, CancellationToken cancellationToken);

    Task<GraphWabaInfo?> GetBusinessAccountAsync(string accessToken, string metaWabaId, CancellationToken cancellationToken);

    Task<IReadOnlyList<GraphPhoneInfo>> GetPhoneNumbersAsync(string accessToken, string metaWabaId, CancellationToken cancellationToken);

    /// <summary>Single-phone status poll — same fields as <see cref="GetPhoneNumbersAsync"/>.</summary>
    Task<GraphPhoneInfo?> GetPhoneNumberAsync(string accessToken, string metaPhoneNumberId, CancellationToken cancellationToken);

    /// <summary>Subscribes our app to the WABA's webhooks (subscribed_apps).</summary>
    Task<GraphOpResult> SubscribeAppAsync(string accessToken, string metaWabaId, CancellationToken cancellationToken);

    /// <summary>Cloud API registration with the two-step verification pin.</summary>
    Task<GraphOpResult> RegisterPhoneAsync(string accessToken, string metaPhoneNumberId, string pin, CancellationToken cancellationToken);

    Task<GraphOpResult> RequestVerificationCodeAsync(
        string accessToken, string metaPhoneNumberId, string codeMethod, string language, CancellationToken cancellationToken);

    Task<GraphOpResult> VerifyCodeAsync(string accessToken, string metaPhoneNumberId, string code, CancellationToken cancellationToken);

    Task<GraphBusinessProfile?> GetBusinessProfileAsync(string accessToken, string metaPhoneNumberId, CancellationToken cancellationToken);

    Task<GraphOpResult> UpdateBusinessProfileAsync(
        string accessToken, string metaPhoneNumberId, GraphBusinessProfile profile, CancellationToken cancellationToken);
}

/// <summary>Success carries the token; failure carries Meta's error message (never both).</summary>
public sealed record GraphTokenResult(bool Success, string? AccessToken, string? Error);

public sealed record GraphOpResult(bool Success, string? Error)
{
    public static readonly GraphOpResult Ok = new(true, null);
    public static GraphOpResult Fail(string error) => new(false, error);
}

public sealed record GraphWabaInfo(
    string MetaWabaId,
    string Name,
    string? Currency,
    string? MessageTemplateNamespace,
    string? BusinessVerificationStatus);

public sealed record GraphPhoneInfo(
    string MetaPhoneNumberId,
    string DisplayPhoneNumber,
    string? VerifiedName,
    string? Status,
    string? CodeVerificationStatus,
    string? NameStatus,
    string? QualityRating,
    string? MessagingTier);

public sealed record GraphBusinessProfile(
    string? About,
    string? Address,
    string? Description,
    string? Email,
    string[] Websites,
    string? Vertical,
    string? ProfilePictureUrl);
