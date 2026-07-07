// ─────────────────────────────────────────────────────────────────────────────
// MetaGraphApiStub — a minimal stand-in for Meta's WhatsApp Business Management API,
// used for local/dev/manual verification of wa-admin-svc flows without real Meta
// credentials (those are user-side, issue #6).
//
// Not a test double used by the automated test suite (WaAdmin.Tests fakes the HTTP layer
// directly for speed/determinism) — this is the literal "stub server you spin up in dev" for
// exercising WaAdmin.WebApi end-to-end against a real HTTP listener.
//
// Surfaces (same shapes as real Graph API):
//   Template submission (issue #16 Task 2):
//     POST /{v}/{wabaId}/message_templates
//       - name starting with "reject_"   -> 400 { "error": { "message": "..." } }
//       - name starting with "error500_" -> 500 (simulates a transient Graph outage)
//       - otherwise                       -> 201 { "id": "<deterministic-fake-id>" }
//   Onboarding (Embedded Signup concept, docs/ONBOARDING_WIZARD_PLAN.md):
//     POST/GET /{v}/oauth/access_token          ES code -> deterministic fake business token
//     GET  /{v}/debug_token?input_token=...     token -> granted WABA id (granular_scopes)
//     GET  /{v}/{wabaId|phoneId}?fields=...     node read: WABA info / phone status polling
//     GET  /{v}/{wabaId}/phone_numbers          the WABA's (single) phone number
//     POST /{v}/{wabaId}/subscribed_apps        webhook subscribe (always succeeds)
//     POST /{v}/{phoneId}/request_code          OTP send simulation
//     POST /{v}/{phoneId}/verify_code           accepts code "000000" only
//     POST /{v}/{phoneId}/register              requires verified number + 6-digit pin
//     GET/POST /{v}/{phoneId}/whatsapp_business_profile   profile get/set (in-memory)
//
// Determinism: ids derive from the ES code (WABA "10"+13 digits, phone "20"+same digits,
// token embeds the digits) so re-running onboarding for the same code upserts the same
// WABA instead of minting duplicates, and debug_token survives stub restarts.
// Review simulation: name_status PENDING_REVIEW -> APPROVED ~30s after registration;
// business_verification_status pending -> verified ~60s after first WABA touch — so the
// wizard's "waiting on Meta review" amber states are demonstrable in dev.
//
// Run: dotnet run --project src/backend/wavio/tools/MetaGraphApiStub
// Default port: 5199 (matches WaAdmin.WebApi's Development fallback for Meta:Graph:BaseUrl).
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5199");
var app = builder.Build();

app.MapGet("/", () => "MetaGraphApiStub — up");

// ── Template submission (unchanged behavior, issue #16) ─────────────────────

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

// ── Onboarding state (in-memory; ids are code-derived so restarts stay coherent) ──

var phones = new ConcurrentDictionary<string, PhoneState>();
var wabaFirstSeen = new ConcurrentDictionary<string, DateTimeOffset>();

// 13 decimal digits derived from the ES code — the shared suffix of the WABA id ("10" + digits)
// and phone id ("20" + digits), also embedded in the token for restart-proof debug_token.
static string DigitsFromCode(string code)
{
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"wavio-es:{code}"));
    var sb = new StringBuilder(13);
    foreach (var b in hash)
    {
        sb.Append((char)('0' + (b % 10)));
        if (sb.Length == 13) break;
    }
    return sb.ToString();
}

PhoneState Phone(string phoneId) => phones.GetOrAdd(phoneId, id => new PhoneState
{
    // "+91 9dddd ddddd" built from the id's digit suffix — stable per WABA.
    DisplayPhoneNumber = $"+91 9{id[^9..^5]} {id[^5..]}",
});

// Token exchange: Meta accepts the ES code via GET query params or POST form/JSON.
async Task<IResult> ExchangeToken(HttpRequest request)
{
    string? code = request.Query["code"];
    if (string.IsNullOrEmpty(code) && request.Method == HttpMethods.Post)
    {
        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync();
            code = form["code"];
        }
        else
        {
            // No ContentLength check: HttpClient's JsonContent streams chunked (no length header).
            try
            {
                var body = await JsonNode.ParseAsync(request.Body);
                code = body?["code"]?.GetValue<string>();
            }
            catch (System.Text.Json.JsonException)
            {
                // empty/non-JSON body — fall through to the missing-code error
            }
        }
    }

    if (string.IsNullOrEmpty(code))
    {
        return Results.Json(new { error = new { message = "Missing authorization code." } },
            statusCode: StatusCodes.Status400BadRequest);
    }

    var digits = DigitsFromCode(code);
    return Results.Json(new
    {
        access_token = $"stub-business-token-{digits}",
        token_type = "bearer",
        expires_in = 5_184_000,
    });
}

app.MapGet("/{version}/oauth/access_token", ExchangeToken);
app.MapPost("/{version}/oauth/access_token", ExchangeToken);

// Token introspection — how the backend discovers which WABA the ES token grants.
app.MapGet("/{version}/debug_token", (string version, string input_token) =>
{
    const string prefix = "stub-business-token-";
    if (!input_token.StartsWith(prefix, StringComparison.Ordinal))
    {
        return Results.Json(new { data = new { is_valid = false } });
    }

    var wabaId = $"10{input_token[prefix.Length..]}";
    return Results.Json(new
    {
        data = new
        {
            app_id = "stub-app",
            is_valid = true,
            granular_scopes = new object[]
            {
                new { scope = "whatsapp_business_management", target_ids = new[] { wabaId } },
                new { scope = "whatsapp_business_messaging", target_ids = new[] { wabaId } },
            },
        },
    });
});

// Node read: /{v}/{wabaId} (WABA info + business verification) or /{v}/{phoneId}
// (registration/OTP/name-review/quality status polling). Meta's ?fields= is accepted but
// the stub always returns the full field set — harmless supersets keep the stub simple.
app.MapGet("/{version}/{nodeId}", (string version, string nodeId) =>
{
    if (nodeId.StartsWith("10", StringComparison.Ordinal))
    {
        var firstSeen = wabaFirstSeen.GetOrAdd(nodeId, _ => DateTimeOffset.UtcNow);
        var verified = DateTimeOffset.UtcNow - firstSeen > TimeSpan.FromSeconds(60);
        return Results.Json(new
        {
            id = nodeId,
            name = "Wavio Demo Business",
            currency = "INR",
            message_template_namespace = $"ns_{nodeId}",
            account_review_status = "APPROVED",
            business_verification_status = verified ? "verified" : "pending",
        });
    }

    if (nodeId.StartsWith("20", StringComparison.Ordinal))
    {
        var phone = Phone(nodeId);
        return Results.Json(phone.ToNode(nodeId));
    }

    return Results.Json(new { error = new { message = $"Unknown node '{nodeId}' (stub)." } },
        statusCode: StatusCodes.Status404NotFound);
});

app.MapGet("/{version}/{wabaId}/phone_numbers", (string version, string wabaId) =>
{
    if (!wabaId.StartsWith("10", StringComparison.Ordinal))
    {
        return Results.Json(new { error = new { message = $"Unknown WABA '{wabaId}' (stub)." } },
            statusCode: StatusCodes.Status404NotFound);
    }

    var phoneId = $"20{wabaId[2..]}";
    return Results.Json(new { data = new[] { Phone(phoneId).ToNode(phoneId) } });
});

app.MapPost("/{version}/{wabaId}/subscribed_apps", (string version, string wabaId) =>
    Results.Json(new { success = true }));

app.MapPost("/{version}/{phoneId}/request_code", (string version, string phoneId) =>
{
    var phone = Phone(phoneId);
    phone.CodeRequested = true;
    return Results.Json(new { success = true });
});

app.MapPost("/{version}/{phoneId}/verify_code", async (string version, string phoneId, HttpRequest request) =>
{
    var body = request.HasFormContentType
        ? JsonNode.Parse($"{{\"code\":\"{(await request.ReadFormAsync())["code"]}\"}}")
        : await JsonNode.ParseAsync(request.Body);
    var code = body?["code"]?.GetValue<string>();

    var phone = Phone(phoneId);
    if (!phone.CodeRequested)
    {
        return Results.Json(new { error = new { message = "No verification code was requested for this number (stub)." } },
            statusCode: StatusCodes.Status400BadRequest);
    }
    if (code != "000000")
    {
        return Results.Json(new { error = new { message = "The verification code is incorrect (stub accepts 000000)." } },
            statusCode: StatusCodes.Status400BadRequest);
    }

    phone.CodeVerified = true;
    return Results.Json(new { success = true });
});

app.MapPost("/{version}/{phoneId}/register", async (string version, string phoneId, HttpRequest request) =>
{
    var body = await JsonNode.ParseAsync(request.Body);
    var pin = body?["pin"]?.GetValue<string>();

    var phone = Phone(phoneId);
    if (!phone.CodeVerified)
    {
        return Results.Json(new { error = new { message = "Phone number is not verified — request and verify a code first (stub)." } },
            statusCode: StatusCodes.Status400BadRequest);
    }
    if (pin is null || pin.Length != 6 || !pin.All(char.IsAsciiDigit))
    {
        return Results.Json(new { error = new { message = "Two-step verification pin must be exactly 6 digits." } },
            statusCode: StatusCodes.Status400BadRequest);
    }

    phone.RegisteredAt ??= DateTimeOffset.UtcNow;
    return Results.Json(new { success = true });
});

app.MapGet("/{version}/{phoneId}/whatsapp_business_profile", (string version, string phoneId) =>
{
    var profile = Phone(phoneId).Profile;
    lock (profile)
    {
        return Results.Json(new
        {
            data = new object[]
            {
                new
                {
                    messaging_product = "whatsapp",
                    about = profile.About,
                    address = profile.Address,
                    description = profile.Description,
                    email = profile.Email,
                    websites = profile.Websites,
                    vertical = profile.Vertical,
                    profile_picture_url = profile.ProfilePictureUrl,
                },
            },
        });
    }
});

app.MapPost("/{version}/{phoneId}/whatsapp_business_profile", async (string version, string phoneId, HttpRequest request) =>
{
    var body = await JsonNode.ParseAsync(request.Body);
    var profile = Phone(phoneId).Profile;
    lock (profile)
    {
        profile.About = body?["about"]?.GetValue<string>() ?? profile.About;
        profile.Address = body?["address"]?.GetValue<string>() ?? profile.Address;
        profile.Description = body?["description"]?.GetValue<string>() ?? profile.Description;
        profile.Email = body?["email"]?.GetValue<string>() ?? profile.Email;
        profile.Vertical = body?["vertical"]?.GetValue<string>() ?? profile.Vertical;
        profile.ProfilePictureUrl = body?["profile_picture_url"]?.GetValue<string>() ?? profile.ProfilePictureUrl;
        if (body?["websites"] is JsonArray sites)
        {
            profile.Websites = [.. sites.Select(s => s!.GetValue<string>())];
        }
    }
    return Results.Json(new { success = true });
});

app.Run();

// Per-phone mutable stub state. Display name review auto-advances ~30s after registration so
// the wizard's PENDING_REVIEW -> APPROVED transition is observable without a real Meta review.
internal sealed class PhoneState
{
    public required string DisplayPhoneNumber { get; init; }
    public bool CodeRequested { get; set; }
    public bool CodeVerified { get; set; }
    public DateTimeOffset? RegisteredAt { get; set; }
    public ProfileState Profile { get; } = new();

    public object ToNode(string phoneId)
    {
        var registered = RegisteredAt is not null;
        var nameApproved = registered && DateTimeOffset.UtcNow - RegisteredAt > TimeSpan.FromSeconds(30);
        return new
        {
            id = phoneId,
            display_phone_number = DisplayPhoneNumber,
            verified_name = "Wavio Demo Business",
            status = registered ? "CONNECTED" : "PENDING",
            code_verification_status = CodeVerified ? "VERIFIED" : "NOT_VERIFIED",
            name_status = !registered ? "NONE" : nameApproved ? "APPROVED" : "PENDING_REVIEW",
            quality_rating = "GREEN",
            messaging_limit_tier = "TIER_1K",
        };
    }
}

internal sealed class ProfileState
{
    public string? About { get; set; }
    public string? Address { get; set; }
    public string? Description { get; set; }
    public string? Email { get; set; }
    public string[] Websites { get; set; } = [];
    public string? Vertical { get; set; }
    public string? ProfilePictureUrl { get; set; }
}
