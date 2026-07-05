using System.Net.Http.Json;
using WaGateway.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace WaGateway.Infrastructure.BillingEstimator;

/// <summary>
/// Calls wa-billing-svc's <c>GET /v1/costs/estimate</c> (spec §4.7, issue #19) for a campaign's
/// pre-launch projected spend (issue #22) — the same service-to-service HTTP hop precedent as
/// <see cref="WaGateway.Infrastructure.WindowState.HttpWindowStateClient"/> (ADR-005's bounded-
/// context-ownership justification: the rate-card tables are wa-billing-svc's, not read directly
/// here). No caching (unlike the window-state client) — this is called once per campaign
/// creation, not once per message on a hot send path.
///
/// Auth: forwards the caller's own bearer token, same as <c>HttpWindowStateClient</c> — this only
/// works because <c>CreateCampaignCommandHandler</c> only ever runs inside an active HTTP request
/// (<c>POST /v1/campaigns</c>), never from a background service.
///
/// Failure handling is NOT fail-closed here (contrast <c>HttpWindowStateClient</c>'s fail-closed
/// "no window" default) — see <see cref="IBillingEstimatorClient"/>'s doc comment: the estimate is
/// advisory only, so an unreachable/erroring estimator must never block campaign creation.
/// </summary>
public sealed partial class HttpBillingEstimatorClient : IBillingEstimatorClient
{
    private readonly HttpClient _http;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<HttpBillingEstimatorClient> _logger;

    public HttpBillingEstimatorClient(
        HttpClient http, IHttpContextAccessor httpContextAccessor, ILogger<HttpBillingEstimatorClient> logger)
    {
        _http = http;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task<BillingEstimateResult?> EstimateAsync(
        string category, string country, Guid phoneNumberId, CancellationToken cancellationToken)
    {
        var authorization = _httpContextAccessor.HttpContext?.Request.Headers[HeaderNames.Authorization].ToString();

        // WaBilling.WebApi's Costs endpoint is mapped at "/v1/costs" (no "/api" prefix — unlike
        // WaGateway's own "/api/v1/messages"/"/api/v1/campaigns"; each service's RoutePrefix is
        // independent, found live: this call 404'd against "/api/v1/costs/estimate" until fixed).
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/v1/costs/estimate?category={Uri.EscapeDataString(category)}&country={Uri.EscapeDataString(country)}" +
            $"&windowOpen=false&phoneNumberId={phoneNumberId}");
        if (!string.IsNullOrEmpty(authorization))
        {
            request.Headers.TryAddWithoutValidation(HeaderNames.Authorization, authorization);
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            LogUnreachable(_logger, ex);
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            LogUnexpectedStatus(_logger, (int)response.StatusCode);
            return null;
        }

        var dto = await response.Content.ReadFromJsonAsync<EstimateHttpEnvelope>(cancellationToken);
        var data = dto?.Data;
        return data is null ? null : new BillingEstimateResult(data.Found, data.Billable, data.Amount, data.Currency);
    }

    /// <summary>Mirrors WaBilling's <c>SingleResponse&lt;CostEstimateDto&gt;</c> response envelope
    /// (wavio.Utilities.ApiResponse) — a local copy, not a shared type, since services don't share
    /// Application-layer DTOs across the process boundary.</summary>
    private sealed record EstimateHttpEnvelope(bool Status, EstimateHttpDto? Data);

    private sealed record EstimateHttpDto(
        bool Found, bool Billable, decimal Amount, string Currency,
        string Category, string Market, string? VolumeTier, Guid? RateCardId, string Reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "wa-billing-svc unreachable for a campaign's pre-launch cost estimate — projected_cost/projected_currency will be left null (advisory only, not fail-closed)")]
    private static partial void LogUnreachable(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "wa-billing-svc returned unexpected HTTP {StatusCode} for a campaign's pre-launch cost estimate")]
    private static partial void LogUnexpectedStatus(ILogger logger, int statusCode);
}
