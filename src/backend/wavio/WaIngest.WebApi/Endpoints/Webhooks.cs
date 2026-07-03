using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WaIngest.Application.Common.Options;
using WaIngest.Application.Ingestion;
using WaIngest.Application.Ingestion.Commands.PersistRawWebhook;
using WaIngest.Application.Ingestion.Commands.ReplayWebhooks;
using WaIngest.Application.Ingestion.Dtos;
using WaIngest.Application.Security;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.Utilities.ApiResponse.ResponseUtil;
using wavio.Utilities.Endpoints;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace WaIngest.WebApi.Endpoints;

/// <summary>
/// GET  /api/v1/webhooks/meta          Meta subscription verification handshake
/// POST /api/v1/webhooks/meta          Webhook delivery: verify → persist raw → ack → async process
/// POST /api/v1/webhooks/meta/replay   Degraded-mode recovery: re-run dedupe/normalize/publish
///                                     for rows a RabbitMQ outage or a crash left unfinished
/// </summary>
public partial class Webhooks : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/webhooks/meta";

    private const string SignatureHeaderName = "X-Hub-Signature-256";

    // Defense in depth beyond Kestrel's global request-size limit: real Meta webhook payloads
    // are a few KB; anything approaching 1MB is not a legitimate delivery.
    private const long MaxBodyBytes = 1_000_000;

    // Persisted alongside the payload for forensics/replay debugging. Never Authorization or any
    // header carrying a secret — the signature header value itself is recorded via
    // SignatureValid (bool), not by copying the header.
    private static readonly string[] PersistedHeaderNames = ["Content-Type", "User-Agent"];

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.WithTags("Webhooks");

        groupBuilder.MapGet(VerifySubscription, "").AllowAnonymous();
        groupBuilder.MapPost(ReceiveWebhook, "").AllowAnonymous();

        // Operational recovery tool, not a tenant-facing endpoint — requires an authenticated
        // platform caller. TODO(#16/#20 follow-up): gate on a dedicated platform-ops permission
        // once the permission catalog has one; "any authenticated user" is the Wave 1 floor.
        groupBuilder.MapPost(Replay, "/replay").RequireAuthorization();
    }

    // ── GET: subscription verification handshake (Meta docs: hub.mode/hub.verify_token/hub.challenge) ─

    private static IResult VerifySubscription(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge,
        IOptions<MetaWebhookOptions> options)
    {
        if (!string.Equals(mode, "subscribe", StringComparison.Ordinal) || string.IsNullOrEmpty(challenge))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        if (!ConstantTimeEquals(verifyToken ?? string.Empty, options.Value.VerifyToken))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        // Meta requires the raw challenge string back, unwrapped — not JSON.
        return Results.Text(challenge, "text/plain");
    }

    private static bool ConstantTimeEquals(string a, string b)
    {
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);
        // FixedTimeEquals requires equal-length spans; a length mismatch is itself not
        // secret-dependent (only the token comparison result would be), so branching on length is fine.
        return aBytes.Length == bBytes.Length && CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }

    // ── POST: webhook delivery ──────────────────────────────────────────────────────────────

    private static async Task<IResult> ReceiveWebhook(
        HttpContext context,
        IDispatcher dispatcher,
        IWebhookIngestBuffer buffer,
        IOptions<MetaWebhookOptions> options,
        ILogger<Webhooks> logger,
        CancellationToken cancellationToken)
    {
        if (context.Request.ContentLength is > MaxBodyBytes)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        // Read the exact raw bytes ONCE, before any parsing. HMAC verification must run against
        // Meta's exact byte stream — re-serializing parsed JSON would never reproduce it
        // byte-for-byte and would always fail signature verification.
        byte[] rawBody;
        await using (var bodyStream = new MemoryStream())
        {
            await context.Request.Body.CopyToAsync(bodyStream, cancellationToken);
            if (bodyStream.Length > MaxBodyBytes)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            rawBody = bodyStream.ToArray();
        }

        // Shape-validate as JSON (no business/typed deserialization — that only ever happens
        // later, off the ack path, in MetaWebhookNormalizer). Required regardless of signature
        // outcome: the jsonb column cannot store anything that isn't valid JSON, and Meta always
        // sends JSON, so a shape failure here means a malformed/non-Meta request, not a security
        // signal worth persisting.
        string payloadText;
        try
        {
            payloadText = Encoding.UTF8.GetString(rawBody);
            using var _ = JsonDocument.Parse(payloadText);
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }

        // Signature check happens BEFORE anything is persisted or dispatched further than the
        // forensic raw-capture below — an invalid/missing signature never reaches the normalizer
        // or the bus (spec §4.3, §5: "reject unsigned/invalid").
        var signatureHeader = context.Request.Headers[SignatureHeaderName].FirstOrDefault();
        var signatureValid = MetaWebhookSignatureVerifier.Verify(rawBody, signatureHeader, options.Value.AppSecret);

        // Persist BEFORE parsing/processing either way (spec §4.3) — signature_valid=false rows
        // are kept for security forensics (who is probing this endpoint) but are never enqueued
        // for dedupe/normalize/publish, and the 401 response tells Meta (or an attacker) nothing
        // about why it failed.
        var headers = PersistedHeaderNames
            .Where(context.Request.Headers.ContainsKey)
            .ToDictionary(h => h, h => context.Request.Headers[h].ToString());
        var headersJson = JsonSerializer.Serialize(headers);

        var reference = await dispatcher.SendAsync(
            new PersistRawWebhookCommand(payloadText, headersJson, signatureValid), cancellationToken);

        if (!signatureValid)
        {
            LogSignatureVerificationFailed(
                logger, signatureHeader is null ? "missing header" : "signature mismatch", reference.Id);
            return Results.Unauthorized();
        }

        // Hand off to the background worker and ack immediately — normalization/dedupe/publish
        // never run on this request (spec §4.3/§8: <500ms p99 ack, "ingest never drops"). A
        // failure to enqueue does not fail the request: the row is already durable and will be
        // picked up by the worker's startup recovery scan or the replay tool.
        try
        {
            await buffer.EnqueueAsync(reference, cancellationToken);
        }
        catch (Exception ex)
        {
            LogEnqueueFailed(logger, ex, reference.Id);
        }

        return Results.Ok();
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Webhook signature verification failed ({Reason}); persisted as raw_webhooks {Id} for forensics, not processed")]
    private static partial void LogSignatureVerificationFailed(ILogger logger, string reason, Guid id);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Failed to enqueue raw_webhooks {Id} for processing — will be recovered by startup scan/replay")]
    private static partial void LogEnqueueFailed(ILogger logger, Exception exception, Guid id);

    // ── POST /replay: degraded-mode recovery ────────────────────────────────────────────────

    private static async Task<IResult> Replay(
        ReplayWebhooksRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(
            new ReplayWebhooksCommand(request.Id, request.Since, request.Until, request.MaxCount ?? 500),
            cancellationToken);

        return Results.Ok(new SingleResponse<ReplayWebhooksResult> { Status = true, Data = result });
    }
}

/// <summary>Request body for POST /api/v1/webhooks/meta/replay — see ReplayWebhooksCommand for the
/// exact semantics of each field (single row by Id, or a Since/Until time window).</summary>
public sealed record ReplayWebhooksRequest(Guid? Id, DateTimeOffset? Since, DateTimeOffset? Until, int? MaxCount);
