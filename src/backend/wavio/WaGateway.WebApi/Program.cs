// ─────────────────────────────────────────────────────────────────────────────
// WaGateway.WebApi (wa-gateway-svc)
// Outbound send API: messages, media, interactive, Flows launch; idempotency, outbox, retries, rate limiting (Wave 1, #14)
//
// Listening port: http://localhost:5101 (dev; see launchSettings.json)
//
// Composition root: wires the wa-gateway-svc bounded-context layers
//   • AddWaGatewayApplication()    → use cases / handlers / validators
//   • AddWaGatewayInfrastructure() → persistence, Meta Graph clients, bus publishers
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
using WaGateway.Application;
using WaGateway.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// ── Request body size cap (security review, PR #45, S4) ────────────────────────
// This host's only body-accepting endpoint is POST /v1/messages — the largest legitimate
// payload (an interactive list with several sections/rows) is a few KB. 256KB is generous
// headroom, not a real limit for any legitimate request. Kestrel enforces this before any model
// binding/handler code runs, returning a clean 413 automatically, so an authenticated tenant
// can't force an oversized single-request parse/allocation as a resource-abuse vector. Same cap
// as wa-admin-svc's (issue #16 security review, S2).
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 262_144);

// ── Aspire ServiceDefaults (OTel, health checks, service discovery, resilience) ─
builder.AddServiceDefaults();

// ── Fail-closed boot guards (security review, PR #45, S2) — see BootGuards.cs for the full
// rationale on each (same lesson already applied to WaIngest issue #13 S2 / WaIntel issue #15 S1,
// applied here at the boot layer too, not just the ctor layer this branch already had). ─────────
BootGuards.RequireRabbitMqConfiguredOutsideDevelopment(builder.Configuration, builder.Environment);
BootGuards.RequireMetaGraphConfiguredOutsideDevelopment(builder.Configuration, builder.Environment);

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

// ── wa-gateway-svc bounded-context composition ───────────────────────────────────────
builder.Services
    .AddWaGatewayApplication()                          // validators + command/query handlers (no mediator)
    .AddWaGatewayInfrastructure(builder.Configuration)  // data surface + external clients
    // The outbox dispatcher runs with no HttpContext — ICurrentTenant must support an explicit
    // tenant override so its RLS-scoped writes work. Must come AFTER AddCurrentTenant() above.
    .ReplaceCurrentTenantWithScopedVersion();

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
app.MapGet("/", () => "WaGateway.WebApi (wa-gateway-svc) — up");

app.Run();
