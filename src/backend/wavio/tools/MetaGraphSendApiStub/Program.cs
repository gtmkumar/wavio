// ─────────────────────────────────────────────────────────────────────────────
// MetaGraphSendApiStub — a minimal stand-in for Meta's WhatsApp Cloud API messages endpoint,
// used for local/dev/manual verification of wa-gateway-svc's outbox → Graph → status flow
// (issue #14) without needing real Meta credentials (those are user-side, issue #6).
//
// Not a test double used by the automated test suite (WaGateway.Tests fakes the client
// interface directly for speed/determinism) — this is the literal "stub server you spin up in
// dev" for exercising the outbox dispatcher end to end against a real HTTP listener, including
// the crash/replay acceptance test.
//
// Endpoints:
//   POST /{version}/{phoneNumberId}/messages
//     Outcome is selected by the request's "to" field (the recipient wa_id) so callers can
//     trigger a specific Graph outcome without needing to embed control fields inside the
//     opaque, type-specific "payload" object:
//       to == "429throttle"    -> 429 (transient — rate limited)
//       to == "500error"       -> 500 (transient — Graph infra outage)
//       to == "131026notfound" -> 400 { error: { code: 131026 } } (permanent — not on WhatsApp)
//       to == "131047reengage" -> 400 { error: { code: 131047 } } (permanent — re-engagement required)
//       to == "131049limit"    -> 400 { error: { code: 131049 } } (permanent — per-user marketing limit)
//       to == "slow45s"        -> waits 45s, then 200 (security review, PR #45, S1: a live-alive
//                                 but slow response, for reproducing/verifying the duplicate-send
//                                 hazard fixed by the Graph client's explicit Timeout — see
//                                 WaGateway.Infrastructure/BackgroundWork/OutboxDispatcherService.cs)
//       anything else          -> 200 { messages: [{ id: "<deterministic-fake-wamid>" }] }
//
// Run: dotnet run --project src/backend/wavio/tools/MetaGraphSendApiStub
// Default port: 5299.
// ─────────────────────────────────────────────────────────────────────────────

using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5299");
var app = builder.Build();

app.MapGet("/", () => "MetaGraphSendApiStub — up");

app.MapPost("/{version}/{phoneNumberId}/messages", async (string version, string phoneNumberId, HttpRequest request) =>
{
    var body = await JsonNode.ParseAsync(request.Body);
    var to = body?["to"]?.GetValue<string>() ?? string.Empty;

    IResult ErrorResult(int statusCode, int errorCode, string message) => Results.Json(
        new { error = new { message, code = errorCode } },
        statusCode: statusCode);

    switch (to)
    {
        case "429throttle":
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        case "500error":
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        case "131026notfound":
            return ErrorResult(400, 131026, "Recipient phone number not a valid WhatsApp user (stub).");
        case "131047reengage":
            return ErrorResult(400, 131047, "Re-engagement message required (stub).");
        case "131049limit":
            return ErrorResult(400, 131049, "Message limit reached for this user (stub, marketing).");
        case "slow45s":
            await Task.Delay(TimeSpan.FromSeconds(45), request.HttpContext.RequestAborted);
            break;
    }

    // Deterministic fake wamid so repeated stub runs are reproducible in manual testing.
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{phoneNumberId}:{to}:{DateTimeOffset.UtcNow.Ticks}"));
    var fakeWamid = "wamid." + Convert.ToHexString(hash)[..24].ToLowerInvariant();

    return Results.Json(new { messages = new[] { new { id = fakeWamid } } }, statusCode: StatusCodes.Status200OK);
});

app.Run();
