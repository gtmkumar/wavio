// ─────────────────────────────────────────────────────────────────────────────
// WaIngest.WebApi (wa-ingest-svc)
// Meta webhook receiver: X-Hub-Signature-256 verify, raw persist, wamid dedupe, normalize, publish to bus (Wave 1, #13)
//
// Listening port: http://localhost:5102 (dev; see launchSettings.json)
//
// Composition root: wires the wa-ingest-svc bounded-context layers
//   • AddWaIngestApplication()    → use cases / handlers / validators
//   • AddWaIngestInfrastructure() → persistence, Meta Graph clients, bus publishers
// plus Aspire ServiceDefaults (OpenTelemetry, health checks, service discovery, resilience).
//
// Auth: validate-only. This host does NOT issue tokens — it validates Identity-issued RS256
// JWTs via the Identity JWKS endpoint (Jwt:Authority). JWKS is fetched lazily on first auth
// request, so the host boots even when Identity is not yet running.
// ─────────────────────────────────────────────────────────────────────────────

using System.Reflection;
using wavio.SharedDataModel;
using wavio.ServiceDefaults.Logging;
using wavio.Utilities.Auth;
using wavio.Utilities.Auth.Audit;
using wavio.Utilities.Endpoints;
using wavio.Utilities.Middlewares.ExceptionsMiddleware;
using wavio.Utilities.OpenApi;
using wavio.Utilities.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.IdentityModel.Tokens;
using WaIngest.Application;
using WaIngest.Application.Common.Options;
using WaIngest.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// ── Aspire ServiceDefaults (OTel, health checks, service discovery, resilience) ─
builder.AddServiceDefaults();

// ── Current user + current tenant (from request principal / JWT claims) ────────
builder.Services.AddCurrentUser();
builder.Services.AddAuditTrail(); // RBAC audit trail: interceptor + IAuditWriter
builder.Services.AddCurrentTenant();

// ── Shared data model: WavioDbContext (+ RLS interceptor wiring) ────────────────
// connStr may be empty — the host must still boot with no ConnectionStrings:Default set.
var connStr = builder.Configuration.GetConnectionString("Default") ?? string.Empty;
builder.Services.AddSharedDataModel(
    connStr,
    builder.Configuration,
    builder.Environment);

// ── Meta webhook config (issue #13: HMAC verify + subscription verify token) ───────────
// Both values are secrets — never logged. Mirrors the shared PII-key startup posture
// (wavio.SharedDataModel/DependencyInjection): Development falls back to a clearly-labelled
// non-secret default; every other environment fails closed at startup.
var metaWebhookSection = builder.Configuration.GetSection(MetaWebhookOptions.SectionName);
var metaAppSecret = metaWebhookSection["AppSecret"];
var metaVerifyToken = metaWebhookSection["VerifyToken"];

if (string.IsNullOrWhiteSpace(metaAppSecret) || string.IsNullOrWhiteSpace(metaVerifyToken))
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException(
            "Meta:Webhook:AppSecret and Meta:Webhook:VerifyToken are required outside Development. " +
            "Provide them via Meta__Webhook__AppSecret / Meta__Webhook__VerifyToken env vars or a secrets provider. " +
            "Wavio will NOT start without them.");

    metaAppSecret ??= "dev-only-not-a-secret-change-me";
    metaVerifyToken ??= "dev-only-verify-token-change-me";
}

builder.Services.Configure<MetaWebhookOptions>(o =>
{
    o.AppSecret = metaAppSecret;
    o.VerifyToken = metaVerifyToken;
});

// ── wa-ingest-svc bounded-context composition ───────────────────────────────────────
builder.Services
    .AddWaIngestApplication()                          // validators + command/query handlers (no mediator)
    .AddWaIngestInfrastructure(builder.Configuration); // data surface + external clients

// ── OpenAPI document (+ Bearer scheme & standard error responses) ──────────────
builder.Services.AddDefaultOpenApi();

// ── JWT Authentication (validate-only; Identity-issued RS256 via JWKS) ─────────
// Authority is REQUIRED — it is the Identity base URL whose JWKS publishes the RS256
// public key. Pin to RS256 to reject "none"/HMAC algorithm-confusion attacks.
var jwtAuthority = builder.Configuration["Jwt:Authority"];
if (string.IsNullOrWhiteSpace(jwtAuthority))
    throw new InvalidOperationException(
        "Jwt:Authority (the Identity issuer base URL whose JWKS publishes the RS256 public key) is required.");

var jwtIssuer   = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.Authority            = jwtAuthority;
        opts.RequireHttpsMetadata = !builder.Environment.IsDevelopment();

        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidIssuer              = jwtIssuer,
            ValidateAudience         = true,
            ValidAudience            = jwtAudience,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ClockSkew                = TimeSpan.FromSeconds(30),
            // Pin to RS256 — reject "none" and HMAC algorithm-confusion attacks.
            ValidAlgorithms          = [SecurityAlgorithms.RsaSha256]
        };
    });

// ── Authorization — shared permission policy provider + handlers ───────────────
builder.Services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, AnyPermissionHandler>();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, StepUpAuthorizationResultHandler>();
builder.Services.AddAuthorization();

var app = builder.Build();

// ── Forwarded headers (prod/staging, behind the gateway/edge proxy) ───────────
app.UseForwardedHeadersIfEnabled();

// -- Wamid-chain correlation (spec 3.2): every log line carries CorrelationId/Wamid --
app.UseWamidCorrelation();

// ── Aspire default health endpoints (/health + /alive) ────────────────────────
app.MapDefaultEndpoints();

// ── Global exception → response-envelope middleware ───────────────────────────
app.UseMiddleware<ExceptionHandler>();

// ── Auth pipeline ──────────────────────────────────────────────────────────────
app.UseAuthentication();
app.UseMiddleware<wavio.Utilities.Middlewares.TenantResolutionMiddleware>();
app.UseAuthorization();

// ── OpenAPI doc (/openapi/v1.json) + Scalar UI (/scalar), dev only ────────────
if (app.Environment.IsDevelopment())
{
    app.MapDefaultOpenApi();
}

// ── Feature endpoints — discovered from IEndpointGroup classes in this assembly ─
app.MapEndpoints(Assembly.GetExecutingAssembly());

// Root liveness.
app.MapGet("/", () => "WaIngest.WebApi (wa-ingest-svc) — up");

app.Run();
