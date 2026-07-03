using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using WaAdmin.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WaAdmin.Infrastructure.Graph;

/// <summary>
/// Thin HTTP client over Meta's WhatsApp Business Management API template endpoint (issue #16
/// Task 2): <c>POST /{version}/{waba-id}/message_templates</c>. Registered as a typed
/// <see cref="HttpClient"/> (see WaAdmin.Infrastructure's DependencyInjection) against
/// <see cref="MetaGraphOptions.BaseUrl"/> — pointed at Meta in production, at a local stub server
/// in dev/tests (see tools/MetaGraphApiStub).
/// </summary>
public sealed partial class MetaGraphTemplateClient : IWhatsAppTemplateGraphClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly MetaGraphOptions _options;
    private readonly ILogger<MetaGraphTemplateClient> _logger;

    public MetaGraphTemplateClient(HttpClient http, IOptions<MetaGraphOptions> options, ILogger<MetaGraphTemplateClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<GraphTemplateSubmitResult> SubmitTemplateAsync(
        GraphTemplateSubmitRequest request, CancellationToken cancellationToken)
    {
        var components = JsonNode.Parse(request.ComponentsJson)
            ?? throw new FormatException("ComponentsJson must be valid JSON.");

        var body = new JsonObject
        {
            ["name"] = request.Name,
            ["language"] = request.Language,
            ["category"] = request.Category.ToUpperInvariant(),
            ["components"] = components,
        };

        var path = $"/{_options.ApiVersion}/{request.BusinessAccountMetaId}/message_templates";

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, options: JsonOptions),
        };
        httpRequest.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.AccessToken);

        using var response = await _http.SendAsync(httpRequest, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var parsed = JsonNode.Parse(responseBody);
            var metaTemplateId = parsed?["id"]?.GetValue<string>();
            if (string.IsNullOrEmpty(metaTemplateId))
            {
                LogUnexpectedSuccessBody(_logger, (int)response.StatusCode);
                return new GraphTemplateSubmitResult(false, null, "Meta accepted the request but returned no template id.");
            }
            return new GraphTemplateSubmitResult(true, metaTemplateId, null);
        }

        var errorMessage = TryExtractErrorMessage(responseBody) ?? $"Meta returned HTTP {(int)response.StatusCode}.";
        LogRejected(_logger, (int)response.StatusCode, errorMessage);
        return new GraphTemplateSubmitResult(false, null, errorMessage);
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
        Message = "Meta template submission rejected: HTTP {StatusCode} — {ErrorMessage}")]
    private static partial void LogRejected(ILogger logger, int statusCode, string errorMessage);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Meta returned a success status ({StatusCode}) with no template id in the body")]
    private static partial void LogUnexpectedSuccessBody(ILogger logger, int statusCode);
}
