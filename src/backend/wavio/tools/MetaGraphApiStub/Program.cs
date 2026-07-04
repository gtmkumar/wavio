// ─────────────────────────────────────────────────────────────────────────────
// MetaGraphApiStub — a minimal stand-in for Meta's WhatsApp Business Management API template
// endpoint, used for local/dev/manual verification of wa-admin-svc's submit-to-Meta flow
// (issue #16 Task 2) without needing real Meta credentials (those are user-side, issue #6).
//
// Not a test double used by the automated test suite (WaAdmin.Tests fakes the HTTP layer
// directly for speed/determinism) — this is the literal "stub server you spin up in dev" for
// exercising WaAdmin.WebApi end-to-end against a real HTTP listener.
//
// Endpoints:
//   POST /{version}/{wabaId}/message_templates
//     - name starting with "reject_"  -> 400 { "error": { "message": "..." } }
//     - name starting with "error500_" -> 500 (simulates a transient Graph outage)
//     - otherwise                      -> 201 { "id": "<deterministic-fake-id>" }
//
// Run: dotnet run --project src/backend/wavio/tools/MetaGraphApiStub
// Default port: 5199 (matches WaAdmin.WebApi's Development fallback for Meta:Graph:BaseUrl).
// ─────────────────────────────────────────────────────────────────────────────

using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5199");
var app = builder.Build();

app.MapGet("/", () => "MetaGraphApiStub — up");

app.MapPost("/{version}/{wabaId}/message_templates", async (string version, string wabaId, HttpRequest request) =>
{
    var body = await JsonNode.ParseAsync(request.Body);
    var name = body?["name"]?.GetValue<string>() ?? string.Empty;

    if (name.StartsWith("reject_", StringComparison.Ordinal))
    {
        return Results.Json(
            new { error = new { message = $"Template name '{name}' failed Meta policy review (stub)." } },
            statusCode: StatusCodes.Status400BadRequest);
    }

    if (name.StartsWith("error500_", StringComparison.Ordinal))
    {
        return Results.StatusCode(StatusCodes.Status500InternalServerError);
    }

    // Deterministic fake id so repeated stub runs are reproducible in manual testing.
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{wabaId}:{name}"));
    var fakeId = Convert.ToHexString(hash)[..16].ToLowerInvariant();

    return Results.Json(new { id = fakeId, status = "PENDING", category = body?["category"]?.GetValue<string>() },
        statusCode: StatusCodes.Status201Created);
});

app.Run();
