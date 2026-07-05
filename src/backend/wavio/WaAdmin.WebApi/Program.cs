// ─────────────────────────────────────────────────────────────────────────────
// WaAdmin.WebApi (wa-admin-svc)
// WABA/phone onboarding (Embedded Signup), template lifecycle, business profile, rate-card sync (Wave 1, #16)
//
// Listening port: http://localhost:5103 (dev; see launchSettings.json)
//
// Composition root: wires the wa-admin-svc bounded-context layers
//   • AddWaAdminApplication()    → use cases / handlers / validators
//   • AddWaAdminInfrastructure() → persistence, Meta Graph clients, bus publishers
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
using WaAdmin.Application;
using WaAdmin.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// ── Aspire ServiceDefaults (OTel, health checks, service discovery, resilience) ─
builder.AddServiceDefaults();

// ── Request body size cap (security review S2, issue #16) ──────────────────────
// This host's only body-accepting endpoints are POST/PUT /v1/templates (a template's compiled
// component JSON is at most a few KB) — 256KB is generous headroom, not a real limit for any
// legitimate request. Kestrel enforces this before any model binding/handler code runs, returning
// a clean 413 automatically, so an authenticated tenant can't force an oversized single-request
// parse/allocation as a resource-abuse vector.
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 262_144);

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

// ── Meta Graph API config (issue #16: template submission) ─────────────────────
// AccessToken is a secret — never logged. Mirrors the shared PII-key / Meta-webhook startup
// posture: Development falls back to a clearly-labelled non-secret default pointed at the local
// stub server (tools/MetaGraphApiStub); every other environment fails closed at startup.
var metaGraphSection = builder.Configuration.GetSection("Meta:Graph");
var metaGraphBaseUrl = metaGraphSection["BaseUrl"];
var metaGraphAccessToken = metaGraphSection["AccessToken"];

if (string.IsNullOrWhiteSpace(metaGraphBaseUrl) || string.IsNullOrWhiteSpace(metaGraphAccessToken))
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException(
            "Meta:Graph:BaseUrl and Meta:Graph:AccessToken are required outside Development. " +
            "Provide them via Meta__Graph__BaseUrl / Meta__Graph__AccessToken env vars or a secrets provider. " +
            "Wavio will NOT start without them.");

    metaGraphBaseUrl ??= "http://localhost:5199"; // tools/MetaGraphApiStub default port
    metaGraphAccessToken ??= "dev-only-not-a-secret-change-me";
}

builder.Configuration["Meta:Graph:BaseUrl"] = metaGraphBaseUrl;
builder.Configuration["Meta:Graph:AccessToken"] = metaGraphAccessToken;

// ── RabbitMq config (issue #16: template-events consumer) — fail closed outside Development ────
// TemplateEventsConsumerBackgroundService's RabbitMqConnectionManager independently refuses the
// same guest:guest@localhost fallback outside Development — this is the fast, eager, boot-time
// half of that same guard (mirrors WaIngest.WebApi's identical check).
var rabbitMqConnStr = builder.Configuration.GetConnectionString("RabbitMq");
if (string.IsNullOrWhiteSpace(rabbitMqConnStr) && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException(
        "ConnectionStrings:RabbitMq is required outside Development. Provide it via " +
        "ConnectionStrings__RabbitMq env var or a secrets provider. Wavio will NOT start without it.");

// ── wa-admin-svc bounded-context composition ───────────────────────────────────────
builder.Services
    .AddWaAdminApplication()                          // validators + command/query handlers (no mediator)
    .AddWaAdminInfrastructure(builder.Configuration); // data surface + external clients

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
        // Fail-closed by default (HTTPS metadata required outside Development). Only an
        // explicit Jwt:RequireHttpsMetadata=false override disables it — reserved for the
        // prod compose's internal core:8080 hop, where Caddy already terminates TLS at the
        // edge and this JWKS fetch never leaves the compose-internal network (S1).
        opts.RequireHttpsMetadata = builder.Configuration.GetValue<bool?>("Jwt:RequireHttpsMetadata")
            ?? !builder.Environment.IsDevelopment();

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

// ── Retention-policy platform defaults (issue #21, spec §4.10) ─────────────────
// Baseline reference data, not sensitive bootstrap credentials — unlike IdentitySeeder this
// deliberately runs in EVERY environment (not Development-only), so every tenant always has a
// platform default to fall back on. Skipped only when no Admin connection string is configured,
// same "boot even with no database" posture as IdentitySeeder's own guard.
var adminConnStr = app.Configuration.GetConnectionString("Admin");
if (string.IsNullOrWhiteSpace(adminConnStr))
{
    app.Logger.LogWarning(
        "RetentionPolicySeeder skipped: no ConnectionStrings:Admin configured.");
}
else
{
    using var seederScope = app.Services.CreateScope();
    var retentionSeeder = new WaAdmin.Infrastructure.Seeders.RetentionPolicySeeder(
        adminConnStr,
        seederScope.ServiceProvider.GetRequiredService<ILogger<WaAdmin.Infrastructure.Seeders.RetentionPolicySeeder>>());
    await retentionSeeder.SeedAsync(CancellationToken.None);
}

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
app.MapGet("/", () => "WaAdmin.WebApi (wa-admin-svc) — up");

app.Run();
