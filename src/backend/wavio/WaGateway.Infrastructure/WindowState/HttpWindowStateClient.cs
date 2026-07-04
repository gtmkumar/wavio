using System.Net.Http.Json;
using WaGateway.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace WaGateway.Infrastructure.WindowState;

/// <summary>
/// Calls wa-intel-svc's <c>GET /v1/windows/{waId}</c> for the pre-dispatch window check
/// (ADR-005) — a service-to-service HTTP hop rather than a direct read of the
/// <c>sessions</c> schema, since that schema is owned by wa-intel-svc (DDD bounded-context
/// ownership). See the issue #14 decisions memory for the full justification, including why a
/// direct DB read was rejected even though it would be faster.
///
/// Auth: forwards the CALLER's own bearer token (the tenant-scoped JWT that authenticated the
/// <c>POST /v1/messages</c> request) rather than minting a separate service-to-service
/// credential — Wave 1 has no dedicated inter-service auth mechanism yet, and the caller's token
/// already carries exactly the tenant claim wa-intel-svc's RLS-scoped endpoint needs. This only
/// works from within an active HTTP request (which is the only place this client is ever called
/// — never from the background outbox dispatcher).
///
/// Caching: a short-TTL <see cref="IMemoryCache"/> entry per (phoneNumberId, waId) bounds the
/// number of calls under load and keeps the p95 &lt;2s send budget intact; a window closing
/// mid-TTL is an accepted Wave 1 edge case (see decisions memory).
/// </summary>
public sealed partial class HttpWindowStateClient : IWindowStateClient
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(5);

    private readonly HttpClient _http;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IMemoryCache _cache;
    private readonly ILogger<HttpWindowStateClient> _logger;

    public HttpWindowStateClient(
        HttpClient http, IHttpContextAccessor httpContextAccessor, IMemoryCache cache, ILogger<HttpWindowStateClient> logger)
    {
        _http = http;
        _httpContextAccessor = httpContextAccessor;
        _cache = cache;
        _logger = logger;
    }

    public async Task<WindowStateResult?> GetWindowStateAsync(Guid phoneNumberId, string waId, CancellationToken cancellationToken)
    {
        var cacheKey = $"windowstate:{phoneNumberId}:{waId}";
        if (_cache.TryGetValue(cacheKey, out WindowStateResult? cached))
        {
            return cached;
        }

        var authorization = _httpContextAccessor.HttpContext?.Request.Headers[HeaderNames.Authorization].ToString();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/windows/{Uri.EscapeDataString(waId)}?phoneNumberId={phoneNumberId}");
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

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // No window row exists yet for this recipient — never messaged before, or it fully
            // expired and hasn't been cleaned up. Either way, no open window.
            var noWindow = new WindowStateResult(false, false);
            _cache.Set(cacheKey, noWindow, CacheTtl);
            return noWindow;
        }

        if (!response.IsSuccessStatusCode)
        {
            LogUnexpectedStatus(_logger, (int)response.StatusCode);
            return null;
        }

        var dto = await response.Content.ReadFromJsonAsync<WindowStateHttpDto>(cancellationToken);
        if (dto is null)
        {
            return null;
        }

        var result = new WindowStateResult(dto.CsOpen, dto.CtwaOpen);
        _cache.Set(cacheKey, result, CacheTtl);
        return result;
    }

    /// <summary>Mirrors WaIntel's <c>WindowStateDto</c> shape (issue #15) — a local copy, not a
    /// shared type, since services don't share Application-layer DTOs across the process boundary.</summary>
    private sealed record WindowStateHttpDto(
        string WaId, Guid PhoneNumberId, string Origin,
        DateTimeOffset? CsExpiresAt, bool CsOpen, DateTimeOffset? CtwaExpiresAt, bool CtwaOpen);

    [LoggerMessage(Level = LogLevel.Warning, Message = "wa-intel-svc unreachable for window-state lookup — treating as no open window (fail closed, ADR-005)")]
    private static partial void LogUnreachable(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "wa-intel-svc returned unexpected HTTP {StatusCode} for window-state lookup")]
    private static partial void LogUnexpectedStatus(ILogger logger, int statusCode);
}
