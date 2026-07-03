// ─────────────────────────────────────────────────────────────────────────────
// core.WebApi — Identity host
//
// Listening port: http://localhost:5050 (dev; fixed — gateway + clients hard-reference it)
//
// Composition root: wires the core bounded-context layers
//   • AddCoreApplication()    → use cases / handlers / validators (core.Application)
//   • AddCoreInfrastructure() → persistence / gateways / external services (core.Infrastructure)
// plus Aspire ServiceDefaults (OpenTelemetry, health checks, service discovery, resilience).
// ─────────────────────────────────────────────────────────────────────────────

using System.Reflection;
using System.Threading.RateLimiting;
using core.Application;
using core.Application.Common;
using core.Application.Common.Interfaces;
using core.Application.Identity.Auth.Common;
using core.Infrastructure;
using core.Infrastructure.Auth;
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

var builder = WebApplication.CreateBuilder(args);

// ── Aspire ServiceDefaults (OTel, service discovery, /health + /alive) ─────────
builder.AddServiceDefaults();

// ── Current user (ICurrentUser from request principal) ─────────────────────────
builder.Services.AddCurrentUser();
builder.Services.AddAuditTrail(); // RBAC audit trail: interceptor + IAuditWriter

// ── Current tenant (ICurrentTenant from JWT claims — backs the shared RLS interceptor) ─
// Cross-cutting registration (wavio.Utilities.Services.HttpContextCurrentTenant),
// shared with other hosts. Without it the shared RlsConnectionInterceptor can't
// resolve ICurrentTenant and DI scope validation fails at builder.Build().
builder.Services.AddCurrentTenant();

// ── Shared data model: WavioDbContext (+ generic repo wiring) ────────────
// NOTE: an ICurrentTenant implementation must also be registered for RLS at runtime.
// connStr is captured into a local so the dev IdentitySeeder block (below, after Build())
// can gate on whether a database is actually configured — the host must still boot with
// no ConnectionStrings:Default set.
var connStr = builder.Configuration.GetConnectionString("Default") ?? string.Empty;
builder.Services.AddSharedDataModel(
    connStr,
    builder.Configuration,
    builder.Environment);

// ── Core bounded-context composition ──────────────────────────────────────────
builder.Services
    .AddCoreApplication()      // validators + command/query handlers (no mediator)
    .AddCoreInfrastructure();  // feature repositories

// ── Auth foundation ────────────────────────────────────────────────────────────
// JWT settings + RS256 signing key. The key provider is eager-constructed so the SAME
// instance backs token issuance (JwtTokenService) AND in-process JWT validation below.
// Development auto-generates+persists a key; outside Development it FAILS CLOSED unless
// Jwt:PrivateKey / Jwt:PrivateKeyPath is supplied.
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));
var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
    ?? throw new InvalidOperationException("Jwt section is required.");

var keyProvider = new RsaJwtKeyProvider(jwtSettings, builder.Environment);
builder.Services.AddSingleton<IJwtKeyProvider>(keyProvider);

builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();

// ── OTP delivery stack ─────────────────────────────────────────────────────────
builder.Services.Configure<OtpSettings>(builder.Configuration.GetSection(OtpSettings.SectionName));

// Fail closed: the testing master OTP (Otp:TestCode) must never reach Production.
if (builder.Environment.IsProduction()
    && !string.IsNullOrEmpty(builder.Configuration[$"{OtpSettings.SectionName}:TestCode"]))
{
    throw new InvalidOperationException(
        "Otp:TestCode is set in a Production environment. The testing master OTP is " +
        "non-production only — remove Otp__TestCode from this environment's configuration.");
}

// OTP delivery: dev sender logs the code. Swap this registration for a real
// SMS/WhatsApp/email provider in production.
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IOtpSender, DevLogOtpSender>();

// ── Seeders ─────────────────────────────────────────────────────────────────────
// Dev-only idempotent identity bootstrap (permissions, system roles, role_permissions,
// tenant, admin user). Runs after Build() — see the gated seed block.
builder.Services.AddScoped<core.Infrastructure.Seeders.IdentitySeeder>();

// ── Rate limiting ──────────────────────────────────────────────────────────────
var authPermitLimit = builder.Configuration.GetValue<int?>("RateLimit:AuthPermitLimit") ?? 10;

builder.Services.AddRateLimiter(opts =>
{
    opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // "auth": 10 req / 60 s per client IP — all /api/v1/auth/* + OAuth backing endpoints.
    opts.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authPermitLimit,
                Window = TimeSpan.FromSeconds(60),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

});

// ── JWT Authentication — single in-process RS256 Bearer scheme ─────────────────
// The in-process signing key is authoritative — no HTTP round-trip to ourselves and
// no startup-order race. Pin to RS256 to reject "none"/HMAC algorithm-confusion attacks.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            // RS256: validate with the in-process public key.
            IssuerSigningKey = keyProvider.SigningKey,
            ClockSkew = TimeSpan.FromSeconds(30),
            // Pin to RS256 — reject "none" and HMAC algorithm-confusion attacks.
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256]
        };
    });

// Permission authorization handlers + dynamic policy provider.
builder.Services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, AnyPermissionHandler>();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
// Step-up: convert a step-up policy denial into a structured 403 step_up_required.
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, StepUpAuthorizationResultHandler>();

builder.Services.AddAuthorization();

// ── OpenAPI document (+ Bearer scheme & standard error responses) ──────────────
builder.Services.AddDefaultOpenApi();

var app = builder.Build();

// ── Run seeders (IdentitySeeder, dev-only idempotent bootstrap) ──────────────
// Seeding runs on the privileged Admin (postgres/superuser) connection — the runtime
// app_user is RLS-enforced and rejects cross-tenant bootstrap INSERTs without a tenant
// context (see SeedingSupport.CreatePrivilegedContext).
//
// Production guard: --seed outside Development throws (no accidental prod seeding).
//
// BOOTABILITY GATE: this host must keep booting even when NO database is configured
// (ConnectionStrings:Default unset). The seeder actively connects to Postgres, so we only
// run it when a connection string is actually present. With a real connection string set,
// dev startup seeds automatically; with none, we log a warning and skip — never crash.
var runSeed = args.Contains("--seed") || app.Environment.IsDevelopment();
if (runSeed)
{
    if (!app.Environment.IsDevelopment())
        throw new InvalidOperationException(
            "--seed is not permitted outside Development. Use a controlled bootstrap process.");

    if (string.IsNullOrWhiteSpace(connStr))
    {
        app.Logger.LogWarning(
            "IdentitySeeder skipped: no ConnectionStrings:Default configured. " +
            "Configure a database connection string to enable Development auto-seeding.");
    }
    else
    {
        using var scope = app.Services.CreateScope();

        // Privileged RLS-bypassing context: prefer ConnectionStrings:Admin (superuser),
        // falling back to the app connection string (Default) in Development.
        using var seedDb = wavio.SharedDataModel.SeedingSupport.CreatePrivilegedContext(
            app.Configuration.GetConnectionString("Admin") ?? connStr);

        var seeder = ActivatorUtilities.CreateInstance<core.Infrastructure.Seeders.IdentitySeeder>(
            scope.ServiceProvider, seedDb);
        await seeder.SeedAsync();
    }
}

// ── Forwarded headers (prod/staging, behind the gateway/edge proxy) ───────────
// Runs first so RemoteIpAddress/scheme reflect the real client. No-op unless
// ForwardedHeaders:Enabled = true. MUST run before UseRateLimiter so rate-limit
// partitioning uses the real client IP.
app.UseForwardedHeadersIfEnabled();

// ── Rate limiting — after real-IP resolution, before endpoints ────────────────
app.UseRateLimiter();

// ── Global exception → response-envelope middleware ───────────────────────────
// Maps ValidationException/BusinessRuleException → 422, UnauthorizedAccessException → 401,
// etc. Runs before auth so handler/validator exceptions surface as clean envelopes.
app.UseMiddleware<ExceptionHandler>();

// ── Aspire default health endpoints (/health + /alive) ────────────────────────
app.MapDefaultEndpoints();

// ── OpenAPI doc (/openapi/v1.json) + Scalar UI (/scalar), dev only ────────────
if (app.Environment.IsDevelopment())
{
    app.MapDefaultOpenApi();
    app.MapGet("/", () => Results.Redirect("/scalar"));
}

// ── Auth pipeline ──────────────────────────────────────────────────────────────
// Order matters: authenticate, then resolve tenant (reads JWT claims, sets RLS bypass
// for platform admins), then authorize.
app.UseAuthentication();

// ── RLS bypass for tenant-context-less pre-auth scope-resolving paths ─────────
// These run as the non-superuser app_user where RLS is active, but legitimately need a
// per-request bypass because no usable token/tenant context exists yet (login, OTP, refresh).
// Each underlying query is keyed to the requester's own id / membership, so isolation holds.
// Runs AFTER authentication (so we only bypass when still unauthenticated) and BEFORE the
// tenant middleware so the bypass flag is set before RLS scoping is decided.
app.Use(async (ctx, next) =>
{
    if (IsScopeResolvingAuthPath(ctx.Request.Path)
        && ctx.User.Identity?.IsAuthenticated != true)
    {
        ctx.Items["bypass_rls"] = true;
    }
    await next(ctx);
});

app.UseMiddleware<wavio.Utilities.Middlewares.TenantResolutionMiddleware>();
app.UseAuthorization();

// ── Feature endpoints — discovered from IEndpointGroup classes in this assembly ─
app.MapEndpoints(Assembly.GetExecutingAssembly());

app.Run();

// ── Pre-auth scope-resolving paths that need an RLS bypass ────────────────────
// Password login, OTP send/verify, and refresh resolve a user's scope/memberships
// before any tenant context exists.
static bool IsScopeResolvingAuthPath(PathString path) =>
    path.StartsWithSegments("/api/v1/auth/password/login")
    || path.StartsWithSegments("/api/v1/auth/otp")
    || path.StartsWithSegments("/api/v1/auth/refresh");
