// ─────────────────────────────────────────────────────────────────────────────
// operations.WebApi — example host, showing the pattern for a second bounded context.
//
// Listening port: http://localhost:5015 (dev; see launchSettings.json)
//
// Composition root: wires the operations bounded-context layers
//   • AddOperationsApplication()    → use cases / handlers / validators (operations.Application)
//   • AddOperationsInfrastructure() → persistence (IOperationsDbContext over the shared context)
// plus Aspire ServiceDefaults (OpenTelemetry, health checks, service discovery, resilience).
//
// Ships with one example CRUD vertical (Widgets — operations.WebApi/Endpoints/Example) to show
// the Entity → DbContext → Application handler → Endpoint pattern to follow for real features.
//
// Auth: validate-only. This host does NOT issue tokens — it validates Identity-issued RS256
// JWTs via the Identity JWKS endpoint (Jwt:Authority). JWKS is fetched lazily on first auth
// request, so the host boots even when Identity is not yet running.
// ─────────────────────────────────────────────────────────────────────────────

using System.Reflection;
using wavio.SharedDataModel;
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
using operations.Application;
using operations.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// ── Aspire ServiceDefaults (OTel, service discovery, /health + /alive) ─────────
builder.AddServiceDefaults();

// ── Current user + current tenant (from request principal / JWT claims) ────────
// One ICurrentTenant adapter (shared HttpContextCurrentTenant) backs the RLS interceptor;
// without it DI scope validation fails at builder.Build().
builder.Services.AddCurrentUser();
builder.Services.AddAuditTrail(); // RBAC audit trail: interceptor + IAuditWriter
builder.Services.AddCurrentTenant();

// ── Shared data model: WavioDbContext (+ RLS interceptor wiring) ─────────
// connStr may be empty — the host must still boot with no ConnectionStrings:Default set.
var connStr = builder.Configuration.GetConnectionString("Default") ?? string.Empty;
builder.Services.AddSharedDataModel(
    connStr,
    builder.Configuration,
    builder.Environment);

// ── Operations bounded-context composition ──────────────────────────────────────
builder.Services
    .AddOperationsApplication()                          // validators + command/query handlers (no mediator)
    .AddOperationsInfrastructure(builder.Configuration); // IOperationsDbContext + file storage over the shared context

// ── OpenAPI document (+ Bearer scheme & standard error responses) ──────────────
builder.Services.AddDefaultOpenApi();

// ── JWT Authentication (validate-only; Identity-issued RS256 via JWKS) ─────────
// Read the three Jwt values directly (operations cannot reference core.Application's
// JwtSettings). Authority is REQUIRED — it is the Identity base URL whose JWKS publishes
// the RS256 public key. Pin to RS256 to reject "none"/HMAC algorithm-confusion attacks.
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
// PermissionPolicyProvider resolves dynamic "permission:<code>" and "permission:<a>|<b>"
// (any-of) policies; handlers evaluate them against the JWT claims.
builder.Services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, AnyPermissionHandler>();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
// Step-up: convert a step-up policy denial into a structured 403 step_up_required.
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, StepUpAuthorizationResultHandler>();
builder.Services.AddAuthorization();

var app = builder.Build();

// ── Forwarded headers (prod/staging, behind the gateway/edge proxy) ───────────
// Runs first so RemoteIpAddress/scheme reflect the real client. No-op unless
// ForwardedHeaders:Enabled = true.
app.UseForwardedHeadersIfEnabled();

// ── Aspire default health endpoints (/health + /alive) ────────────────────────
app.MapDefaultEndpoints();

// ── Global exception → response-envelope middleware ───────────────────────────
// Maps ValidationException/BusinessRuleException → 422, UnauthorizedAccessException → 401, etc.
// Runs before auth so handler/validator exceptions surface as clean envelopes.
app.UseMiddleware<ExceptionHandler>();

// ── Auth pipeline ──────────────────────────────────────────────────────────────
// Shared TenantResolutionMiddleware (wavio.Utilities) runs after authentication:
// platform admins get RLS bypass + X-Tenant-Id → tenant_id_override so RequireTenantId() resolves.
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
app.MapGet("/", () => "operations.WebApi — up (example host, see Endpoints/Example/Widgets.cs)");

app.Run();
