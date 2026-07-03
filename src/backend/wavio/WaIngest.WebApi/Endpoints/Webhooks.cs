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
    // are a few KB; anything approaching 1MB is not a legitimate delivery. Enforced by bounding
    // the read itself (TryReadBoundedBodyAsync), not just by trusting the Content-Length header —
    // a chunked-encoded request has no Content-Length and would otherwise bypass this entirely.
    internal const long MaxBodyBytes = 1_000_000;

    // Persisted alongside the payload for forensics/replay debugging. Never Authorization or any
    // header carrying a secret — the signature header value itself is recorded via
    // SignatureValid (bool), not by copying the header.
    private static readonly string[] PersistedHeaderNames = ["Content-Type", "User-Agent"];

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.WithTags("Webhooks");

        groupBuilder.MapGet(VerifySubscription, "").AllowAnonymous();
        groupBuilder.MapPost(ReceiveWebhook, "").AllowAnonymous();

        // Operational recovery tool, not a tenant-facing endpoint. Gated on a permission code
        // ("ingest.webhooks.replay") that is deliberately NOT in core's seeded permission
        // catalog (core.Infrastructure/Seeders/IdentitySeeder.cs) — no tenant-scoped role can
        // ever be granted it, so only a platform_admin JWT passes, via PermissionHandler's
        // Gate 2 bypass (user_type == platform_admin). If a non-platform-admin operator role
        // should ever need this, add the permission definition + a grant there first.
        groupBuilder.MapPost(Replay, "/replay").RequireAuthorization("permission:ingest.webhooks.replay");
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

    // internal (not private): WaIngest.Tests exercises this directly against a fabricated
    // HttpContext to prove the signature-invalid path never persists/parses the real body
    // (see WaIngest.WebApi.csproj's InternalsVisibleTo).
    internal static async Task<IResult> ReceiveWebhook(
        HttpContext context,
        IDispatcher dispatcher,
        IWebhookIngestBuffer buffer,
        IOptions<MetaWebhookOptions> options,
        ILogger<Webhooks> logger,
        CancellationToken cancellationToken)
    {
        if (context.Request.ContentLength is > MaxBodyBytes)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        // Bounded read: caps memory regardless of Content-Length/chunked transfer-encoding —
        // aborts as soon as the cumulative byte count would exceed MaxBodyBytes, so a malicious
        // or buggy chunked sender never gets more than one buffer's worth over the limit
        // resident in memory.
        var rawBody = await TryReadBoundedBodyAsync(context.Request.Body, cancellationToken);
        if (rawBody is null)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        // Signature verification runs BEFORE any parsing or persistence of the request body
        // (spec §4.3/§5: "reject unsigned/invalid"; reject-before-deserialize). An
        // unauthenticated caller's bytes are never written to Postgres and never handed to a
        // JSON parser — only a small, fixed-shape stub is persisted, purely so we retain a
        // forensic count/timestamp of probing attempts without giving an attacker a way to fill
        // the shared database with arbitrary bytes.
        var signatureHeader = context.Request.Headers[SignatureHeaderName].FirstOrDefault();
        var signatureValid = MetaWebhookSignatureVerifier.Verify(rawBody, signatureHeader, options.Value.AppSecret);

        if (!signatureValid)
        {
            var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var reason = signatureHeader is null ? "missing header" : "signature mismatch";

            var stubPayload = JsonSerializer.Serialize(new { note = "signature_invalid", bytes = rawBody.Length });
            var stubHeadersJson = JsonSerializer.Serialize(CollectPersistedHeaders(context));

            var stubReference = await dispatcher.SendAsync(
                new PersistRawWebhookCommand(stubPayload, stubHeadersJson, SignatureValid: false), cancellationToken);

            LogSignatureVerificationFailed(logger, reason, remoteIp, stubReference.Id);
            return Results.Unauthorized();
        }

        // Only now — with the signature verified — do we touch the real body at all. Shape-
        // validate as JSON (no business/typed deserialization; that only ever happens later,
        // off the ack path, in MetaWebhookNormalizer) purely because the jsonb column requires
        // valid JSON. Meta always sends JSON when it signs a payload, so failing here would mean
        // something is deeply wrong on Meta's side, not an attack — handled defensively anyway.
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

        var headersJson = JsonSerializer.Serialize(CollectPersistedHeaders(context));

        var reference = await dispatcher.SendAsync(
            new PersistRawWebhookCommand(payloadText, headersJson, SignatureValid: true), cancellationToken);

        // Hand off to the background worker and ack immediately — normalization/dedupe/publish
        // never run on this request (spec §4.3/§8: <500ms p99 ack, "ingest never drops"). A
        // failure to enqueue does not fail the request: the row is already durable and will be
        // picked up by the worker's periodic recovery sweep or the replay tool.
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

    /// <summary>Reads the request body up to <see cref="MaxBodyBytes"/>; returns null (caller
    /// should respond 413) the instant the cumulative byte count would exceed it. Bounds memory
    /// even for chunked-encoded requests, which carry no Content-Length to pre-check.</summary>
    internal static async Task<byte[]?> TryReadBoundedBodyAsync(Stream body, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
                return null;
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
        return buffer.ToArray();
    }

    private static Dictionary<string, string> CollectPersistedHeaders(HttpContext context) =>
        PersistedHeaderNames
            .Where(context.Request.Headers.ContainsKey)
            .ToDictionary(h => h, h => context.Request.Headers[h].ToString());

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Webhook signature verification failed ({Reason}) from {RemoteIp}; persisted stub as raw_webhooks {Id} for forensics, not processed")]
    private static partial void LogSignatureVerificationFailed(ILogger logger, string reason, string remoteIp, Guid id);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Failed to enqueue raw_webhooks {Id} for processing — will be recovered by the periodic sweep/replay")]
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
