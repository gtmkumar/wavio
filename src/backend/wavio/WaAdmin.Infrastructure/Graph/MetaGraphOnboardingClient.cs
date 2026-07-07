using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using WaAdmin.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WaAdmin.Infrastructure.Graph;

/// <summary>
/// Graph API client for the onboarding wizard (docs/ONBOARDING_WIZARD_PLAN.md) — same typed
/// HttpClient pattern as <see cref="MetaGraphTemplateClient"/>, pointed at
/// <see cref="MetaGraphOptions.BaseUrl"/> (real Meta in production, tools/MetaGraphApiStub in
/// dev). Access tokens arrive per call (decrypted per-WABA business tokens) and are only ever
/// placed in the Authorization header — never in URLs, never logged.
/// </summary>
public sealed partial class MetaGraphOnboardingClient : IWhatsAppOnboardingGraphClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly MetaGraphOptions _options;
    private readonly ILogger<MetaGraphOnboardingClient> _logger;

    public MetaGraphOnboardingClient(
        HttpClient http, IOptions<MetaGraphOptions> options, ILogger<MetaGraphOnboardingClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<GraphTokenResult> ExchangeCodeAsync(string code, CancellationToken cancellationToken)
    {
        // Meta's ES exchange is a POST with client credentials + the popup's code. The stub
        // ignores client_id/client_secret, so empty values are fine in dev.
        var body = new JsonObject
        {
            ["client_id"] = _options.AppId,
            ["client_secret"] = _options.AppSecret,
            ["code"] = code,
        };

        using var response = await _http.PostAsync(
            $"/{_options.ApiVersion}/oauth/access_token",
            JsonContent.Create(body, options: JsonOptions),
            cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = TryExtractErrorMessage(responseBody) ?? $"Meta returned HTTP {(int)response.StatusCode}.";
            LogGraphRejected(_logger, "oauth/access_token", (int)response.StatusCode, error);
            return new GraphTokenResult(false, null, error);
        }

        var token = JsonNode.Parse(responseBody)?["access_token"]?.GetValue<string>();
        return string.IsNullOrEmpty(token)
            ? new GraphTokenResult(false, null, "Meta returned no access token.")
            : new GraphTokenResult(true, token, null);
    }

    public async Task<IReadOnlyList<string>> GetGrantedWabaIdsAsync(string accessToken, CancellationToken cancellationToken)
    {
        // input_token identifies the token being inspected; the app token authorizes the call.
        // Both are the same business token here — sufficient for granular_scopes and what the
        // stub expects. debug_token's API shape forces the token into the query string, so this
        // typed client is registered with RemoveAllLoggers() — HttpClientFactory's default
        // handlers would otherwise log the full request URI (token included) at Information.
        var node = await GetJsonAsync(
            accessToken,
            $"/{_options.ApiVersion}/debug_token?input_token={Uri.EscapeDataString(accessToken)}",
            "debug_token",
            cancellationToken);

        if (node?["data"]?["granular_scopes"] is not JsonArray scopes) return [];

        return [.. scopes
            .Where(s => s?["scope"]?.GetValue<string>() == "whatsapp_business_management")
            .SelectMany(s => s?["target_ids"] as JsonArray ?? [])
            .Select(id => id!.GetValue<string>())
            .Distinct()];
    }

    public async Task<GraphWabaInfo?> GetBusinessAccountAsync(
        string accessToken, string metaWabaId, CancellationToken cancellationToken)
    {
        var node = await GetJsonAsync(
            accessToken,
            $"/{_options.ApiVersion}/{metaWabaId}?fields=id,name,currency,message_template_namespace,business_verification_status",
            "waba node",
            cancellationToken);
        if (node is null) return null;

        return new GraphWabaInfo(
            node["id"]?.GetValue<string>() ?? metaWabaId,
            node["name"]?.GetValue<string>() ?? "WhatsApp Business Account",
            node["currency"]?.GetValue<string>(),
            node["message_template_namespace"]?.GetValue<string>(),
            node["business_verification_status"]?.GetValue<string>());
    }

    public async Task<IReadOnlyList<GraphPhoneInfo>> GetPhoneNumbersAsync(
        string accessToken, string metaWabaId, CancellationToken cancellationToken)
    {
        var node = await GetJsonAsync(
            accessToken,
            $"/{_options.ApiVersion}/{metaWabaId}/phone_numbers?fields={PhoneFields}",
            "phone_numbers",
            cancellationToken);

        if (node?["data"] is not JsonArray rows) return [];
        return [.. rows.OfType<JsonObject>().Select(ParsePhone)];
    }

    public async Task<GraphPhoneInfo?> GetPhoneNumberAsync(
        string accessToken, string metaPhoneNumberId, CancellationToken cancellationToken)
    {
        var node = await GetJsonAsync(
            accessToken,
            $"/{_options.ApiVersion}/{metaPhoneNumberId}?fields={PhoneFields}",
            "phone node",
            cancellationToken);
        return node is JsonObject obj ? ParsePhone(obj) : null;
    }

    public Task<GraphOpResult> SubscribeAppAsync(string accessToken, string metaWabaId, CancellationToken cancellationToken) =>
        PostForSuccessAsync(accessToken, $"/{_options.ApiVersion}/{metaWabaId}/subscribed_apps", null, "subscribed_apps", cancellationToken);

    public Task<GraphOpResult> RegisterPhoneAsync(
        string accessToken, string metaPhoneNumberId, string pin, CancellationToken cancellationToken) =>
        PostForSuccessAsync(
            accessToken,
            $"/{_options.ApiVersion}/{metaPhoneNumberId}/register",
            new JsonObject { ["messaging_product"] = "whatsapp", ["pin"] = pin },
            "register",
            cancellationToken);

    public Task<GraphOpResult> RequestVerificationCodeAsync(
        string accessToken, string metaPhoneNumberId, string codeMethod, string language, CancellationToken cancellationToken) =>
        PostForSuccessAsync(
            accessToken,
            $"/{_options.ApiVersion}/{metaPhoneNumberId}/request_code",
            new JsonObject { ["code_method"] = codeMethod, ["language"] = language },
            "request_code",
            cancellationToken);

    public Task<GraphOpResult> VerifyCodeAsync(
        string accessToken, string metaPhoneNumberId, string code, CancellationToken cancellationToken) =>
        PostForSuccessAsync(
            accessToken,
            $"/{_options.ApiVersion}/{metaPhoneNumberId}/verify_code",
            new JsonObject { ["code"] = code },
            "verify_code",
            cancellationToken);

    public async Task<GraphBusinessProfile?> GetBusinessProfileAsync(
        string accessToken, string metaPhoneNumberId, CancellationToken cancellationToken)
    {
        var node = await GetJsonAsync(
            accessToken,
            $"/{_options.ApiVersion}/{metaPhoneNumberId}/whatsapp_business_profile",
            "business profile",
            cancellationToken);

        if (node?["data"] is not JsonArray { Count: > 0 } rows || rows[0] is not JsonObject profile) return null;

        return new GraphBusinessProfile(
            profile["about"]?.GetValue<string>(),
            profile["address"]?.GetValue<string>(),
            profile["description"]?.GetValue<string>(),
            profile["email"]?.GetValue<string>(),
            profile["websites"] is JsonArray sites ? [.. sites.Select(s => s!.GetValue<string>())] : [],
            profile["vertical"]?.GetValue<string>(),
            profile["profile_picture_url"]?.GetValue<string>());
    }

    public Task<GraphOpResult> UpdateBusinessProfileAsync(
        string accessToken, string metaPhoneNumberId, GraphBusinessProfile profile, CancellationToken cancellationToken)
    {
        var body = new JsonObject { ["messaging_product"] = "whatsapp" };
        if (profile.About is not null) body["about"] = profile.About;
        if (profile.Address is not null) body["address"] = profile.Address;
        if (profile.Description is not null) body["description"] = profile.Description;
        if (profile.Email is not null) body["email"] = profile.Email;
        if (profile.Websites.Length > 0) body["websites"] = new JsonArray([.. profile.Websites.Select(w => JsonValue.Create(w))]);
        if (profile.Vertical is not null) body["vertical"] = profile.Vertical;
        if (profile.ProfilePictureUrl is not null) body["profile_picture_url"] = profile.ProfilePictureUrl;

        return PostForSuccessAsync(
            accessToken,
            $"/{_options.ApiVersion}/{metaPhoneNumberId}/whatsapp_business_profile",
            body,
            "business profile update",
            cancellationToken);
    }

    // ── shared plumbing ──────────────────────────────────────────────────────

    private const string PhoneFields =
        "id,display_phone_number,verified_name,status,code_verification_status,name_status,quality_rating,messaging_limit_tier";

    private static GraphPhoneInfo ParsePhone(JsonObject node) => new(
        node["id"]?.GetValue<string>() ?? string.Empty,
        node["display_phone_number"]?.GetValue<string>() ?? string.Empty,
        node["verified_name"]?.GetValue<string>(),
        node["status"]?.GetValue<string>(),
        node["code_verification_status"]?.GetValue<string>(),
        node["name_status"]?.GetValue<string>(),
        node["quality_rating"]?.GetValue<string>(),
        node["messaging_limit_tier"]?.GetValue<string>());

    private async Task<JsonNode?> GetJsonAsync(
        string accessToken, string path, string operation, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _http.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            LogGraphRejected(_logger, operation, (int)response.StatusCode,
                TryExtractErrorMessage(responseBody) ?? "no error body");
            return null;
        }

        return JsonNode.Parse(responseBody);
    }

    private async Task<GraphOpResult> PostForSuccessAsync(
        string accessToken, string path, JsonObject? body, string operation, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null) request.Content = JsonContent.Create(body, options: JsonOptions);

        using var response = await _http.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode) return GraphOpResult.Ok;

        var error = TryExtractErrorMessage(responseBody) ?? $"Meta returned HTTP {(int)response.StatusCode}.";
        LogGraphRejected(_logger, operation, (int)response.StatusCode, error);
        return GraphOpResult.Fail(error);
    }

    private static string? TryExtractErrorMessage(string responseBody)
    {
        try
        {
            var node = JsonNode.Parse(responseBody);
            return node?["error"]?["message"]?.GetValue<string>();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Meta Graph {Operation} call rejected: HTTP {StatusCode} — {ErrorMessage}")]
    private static partial void LogGraphRejected(ILogger logger, string operation, int statusCode, string errorMessage);
}
