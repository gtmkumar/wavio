using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using WaGateway.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WaGateway.Infrastructure.Graph;

/// <summary>
/// Thin HTTP client over Meta's WhatsApp Cloud API messages endpoint (issue #14):
/// <c>POST /{version}/{phone-number-id}/messages</c>. Registered as a typed <see cref="HttpClient"/>
/// (see WaGateway.Infrastructure's DependencyInjection) against <see cref="MetaGraphOptions.BaseUrl"/>
/// — pointed at Meta in production, at a local stub server in dev/tests
/// (see tools/MetaGraphSendApiStub).
///
/// The request body is a deliberate simplification of Meta's real per-type message object shapes
/// (a faithful reimplementation of all 11 typed payloads' exact Graph wire format is real-Meta
/// integration work that belongs with WABA onboarding, issue #6 — this client and its stub are
/// enough to prove the outbox → Graph → status round trip end to end for Wave 1).
/// </summary>
public sealed partial class MetaGraphMessageClient : IMetaGraphMessageClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly MetaGraphOptions _options;
    private readonly ILogger<MetaGraphMessageClient> _logger;

    public MetaGraphMessageClient(HttpClient http, IOptions<MetaGraphOptions> options, ILogger<MetaGraphMessageClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<GraphSendResult> SendAsync(GraphSendRequest request, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["messaging_product"] = "whatsapp",
            ["to"] = request.ToWaId,
            ["type"] = request.MessageType,
            ["payload"] = JsonNode.Parse(request.PayloadJson),
        };

        var path = $"/{_options.ApiVersion}/{request.MetaPhoneNumberId}/messages";

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, options: JsonOptions),
        };
        // Never logged — read directly from config, never surfaced in any log statement below.
        httpRequest.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.AccessToken);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(httpRequest, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            // Connection-level failure (broker/stub unreachable) — treat as transient, same as a 5xx.
            LogConnectionFailure(_logger, ex);
            return new GraphSendResult(false, null, IsTransientFailure: true, "CONNECTION_ERROR", ex.Message);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // The CALLER's own token was NOT what fired — this is HttpClient's own Timeout
            // elapsing (security review, PR #45, S1: the client now has an explicit Timeout
            // strictly less than Outbox:StaleLockSeconds — see DependencyInjection.cs — so this
            // path failing fast, classified transient, is what lets the outbox dispatcher's
            // normal retry/backoff handle a slow Graph response instead of a stale-lease
            // reclaim racing an still-in-flight call and double-sending).
            LogTimeout(_logger, ex);
            return new GraphSendResult(false, null, IsTransientFailure: true, "TIMEOUT", "Graph request timed out.");
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var parsed = JsonNode.Parse(responseBody);
            var wamid = parsed?["messages"]?[0]?["id"]?.GetValue<string>();
            if (string.IsNullOrEmpty(wamid))
            {
                LogUnexpectedSuccessBody(_logger, (int)response.StatusCode);
                return new GraphSendResult(false, null, IsTransientFailure: true, "NO_WAMID", "Meta accepted the request but returned no message id.");
            }
            return new GraphSendResult(true, wamid, IsTransientFailure: false, null, null);
        }

        var (errorCode, errorMessage) = TryExtractError(responseBody);
        var isTransient = GraphErrorClassifier.IsTransient((int)response.StatusCode, errorCode);
        LogRejected(_logger, (int)response.StatusCode, errorCode, isTransient);

        return new GraphSendResult(
            false, null, isTransient,
            errorCode?.ToString(CultureInfo.InvariantCulture) ?? ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture),
            errorMessage ?? $"Meta returned HTTP {(int)response.StatusCode}.");
    }

    private static (int? Code, string? Message) TryExtractError(string responseBody)
    {
        try
        {
            var node = JsonNode.Parse(responseBody);
            var code = node?["error"]?["code"]?.GetValue<int>();
            var message = node?["error"]?["message"]?.GetValue<string>();
            return (code, message);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Graph send failed with HTTP {StatusCode}, errorCode={ErrorCode}, transient={IsTransient}")]
    private static partial void LogRejected(ILogger logger, int statusCode, int? errorCode, bool isTransient);

    [LoggerMessage(Level = LogLevel.Error, Message = "Graph returned success HTTP {StatusCode} but no parseable wamid")]
    private static partial void LogUnexpectedSuccessBody(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Graph send failed: connection error")]
    private static partial void LogConnectionFailure(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Graph send failed: request timed out")]
    private static partial void LogTimeout(ILogger logger, Exception exception);
}
