// Wavio — API Gateway
//
// Listening port: http://localhost:8080 (dev)
//
// Responsibilities:
//   - Path-prefix routing via YARP. Path prefixes fan out to each service:
//       /identity    → core       @5050 (auth/users/roles)
//       /operations  → operations @5002 (example Widgets vertical — add real routes as you build)
//       /commerce    → commerce   @5005 (minimal second host)
//   - Central CORS (single point for all clients)
//   - Global per-IP rate limiting (fixed-window, 300 req/min)
//   - Security response headers (mirrors ServiceDefaults.UseSecurityHeaders)
//   - Forwarding: Authorization, X-Tenant-Id, X-Forwarded-For/Proto/Host (YARP default)
//   - Aggregate health: GET /health/services fans out to each service's /health/ready
//
// ADDITIVE: the per-service direct ports (:5050, :5002, :5005) remain fully operational.
// Clients can switch from per-service base URLs to a single http://localhost:8080
// without any URL-path changes — the first path segment selects the upstream.

using System.Net;
using System.Threading.RateLimiting;
using wavio.Gateway;
using Microsoft.AspNetCore.RateLimiting;
using Yarp.ReverseProxy.Configuration;

var builder = WebApplication.CreateBuilder(args);

// ── Aspire ServiceDefaults (OTel, service discovery, /health + /alive) ────────
builder.AddServiceDefaults();

// ── YARP — load route/cluster config from our strongly-typed config section ───
//
// We build the YARP config programmatically from appsettings.json rather than
// using the built-in YARP config section so that:
//   a) We can strip path prefixes in a type-safe way alongside the routes.
//   b) Cluster destinations can be overridden per-environment without knowing
//      YARP's internal config key names (our keys are simpler).
//
// Routes are statically defined here; cluster destinations come from config so
// that production can point at real hosts via env-var overrides.

var gatewaySection = builder.Configuration.GetSection("Gateway:Clusters");

RouteConfig MakeRoute(string routeId, string pathPrefix, string clusterId) =>
    new()
    {
        RouteId   = routeId,
        ClusterId = clusterId,
        Match     = new RouteMatch { Path = $"/{pathPrefix}/{{**catch-all}}" },
        // Strip the leading prefix segment before forwarding.
        // E.g. /identity/api/v1/auth/login → /api/v1/auth/login at :5050
        Transforms = [new Dictionary<string, string> { ["PathPattern"] = "/{**catch-all}" }]
    };

ClusterConfig MakeCluster(string clusterId)
{
    var address = gatewaySection[$"{clusterId}:Destinations:primary:Address"]
        ?? throw new InvalidOperationException(
            $"Gateway:Clusters:{clusterId}:Destinations:primary:Address is required.");

    return new ClusterConfig
    {
        ClusterId    = clusterId,
        Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase)
        {
            ["primary"] = new DestinationConfig { Address = address }
        }
    };
}

// One path prefix per service. Add a MakeRoute/MakeCluster pair here for each new service
// you stand up (mirroring the AppHost.cs + appsettings.json Gateway:Clusters entries).
var routes = new[]
{
    MakeRoute("identity-route",   "identity",   "identity"),
    MakeRoute("operations-route", "operations", "operations"),
    MakeRoute("commerce-route",   "commerce",   "commerce"),
};

var clusters = new[]
{
    MakeCluster("identity"),
    MakeCluster("operations"),
    MakeCluster("commerce"),
};

builder.Services
    .AddReverseProxy()
    .LoadFromMemory(routes, clusters);

// ── CORS — single central policy for all gateway-routed clients ───────────────
//
// Dev: allows the two Vite dev server origins (admin-web :5173, pos-web :5174)
//      with credentials so Authorization cookies/headers work.
// Non-dev: origins loaded from Cors:AllowedOrigins config section.
//
// When clients adopt the gateway as their single base URL this becomes the
// only CORS point — individual services keep their own CORS for direct access.

const string GatewayCorsPolicyName = "GatewayCors";

builder.Services.AddCors(opts =>
{
    if (builder.Environment.IsDevelopment())
    {
        opts.AddPolicy(GatewayCorsPolicyName, policy =>
            policy
                .WithOrigins("http://localhost:5173", "http://localhost:5174")
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials());
    }
    else
    {
        var configuredOrigins = builder.Configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? [];

        opts.AddPolicy(GatewayCorsPolicyName, policy =>
            policy
                .WithOrigins(configuredOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials());
    }
});

// ── Global rate limiter — per client-IP, fixed window ─────────────────────────
//
// 300 requests / 60 seconds per IP by default (config-driven).
// Client IP is resolved from X-Forwarded-For when that header is present
// (YARP forwards it by default; the real edge proxy sets it before us in prod).
// Auth paths (/identity/connect/*, /identity/api/v1/auth/*) also hit Identity's
// own stricter limiter — this global cap is an outer backstop only.
//
// On limit breach: HTTP 429 Too Many Requests (standard IETF status).

var rateLimitSection = builder.Configuration.GetSection("RateLimit");
var permitLimit       = rateLimitSection.GetValue<int>("PermitLimit",  300);
var windowSeconds     = rateLimitSection.GetValue<int>("WindowSeconds", 60);

const string GlobalRateLimiterPolicy = "GlobalPerIp";

builder.Services.AddRateLimiter(limiterOpts =>
{
    limiterOpts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    limiterOpts.AddFixedWindowLimiter(GlobalRateLimiterPolicy, opts =>
    {
        opts.PermitLimit         = permitLimit;
        opts.Window              = TimeSpan.FromSeconds(windowSeconds);
        opts.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opts.QueueLimit          = 0; // reject immediately when window full
    });

    // Partition per client IP, honouring X-Forwarded-For so the correct IP
    // is used when requests pass through the Aspire / Docker network layer.
    limiterOpts.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        // Try X-Forwarded-For first (set by upstream proxy / YARP itself in prod)
        var forwardedFor = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        var clientIp     = !string.IsNullOrWhiteSpace(forwardedFor)
            ? forwardedFor.Split(',')[0].Trim()          // take leftmost (real client)
            : ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(clientIp, _ =>
            new FixedWindowRateLimiterOptions
            {
                PermitLimit          = permitLimit,
                Window               = TimeSpan.FromSeconds(windowSeconds),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit           = 0
            });
    });
});

// ── Aggregate health HttpClient — used by /health/services endpoint ────────────
//
// Named client "HealthCheck" with a short 3-second timeout per service.
// Services' /health/ready endpoints are probed in parallel.

builder.Services.AddHttpClient(HealthServicesEndpoint.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(3);
});

// ──────────────────────────────────────────────────────────────────────────────

var app = builder.Build();

// ── Middleware pipeline ────────────────────────────────────────────────────────
//
// Ordering rationale:
//   1. Security headers — added to every response including preflight rejections.
//   2. CORS — must run before rate limiting so OPTIONS preflight is not counted
//      against the per-IP quota and returns correct headers even on rejection.
//   3. Rate limiting — after CORS so preflight never burns the caller's quota.
//   4. YARP proxy — terminal; routes matched requests to upstream services.
//      YARP by default forwards X-Forwarded-For, X-Forwarded-Proto,
//      X-Forwarded-Host and passes through Authorization and X-Tenant-Id untouched.

// Rewrite RemoteIpAddress/scheme from X-Forwarded-* (prod/staging, behind the edge proxy).
// Must run first so the per-IP rate limiter and security headers see the real client.
// No-op unless ForwardedHeaders:Enabled = true.
app.UseForwardedHeadersIfEnabled();

// No-op in Development (mirrors ServiceDefaults.UseSecurityHeaders behaviour).
app.UseSecurityHeaders();

app.UseCors(GatewayCorsPolicyName);

app.UseRateLimiter();

// ── /health/services — aggregate fan-out endpoint ─────────────────────────────
app.MapHealthServicesEndpoint(builder.Configuration);

// ── Aspire default health endpoints (/health + /alive, Development only) ───────
app.MapDefaultEndpoints();

// ── YARP reverse proxy — terminal middleware ──────────────────────────────────
//
// YARP's default ForwardedHeadersTransform adds X-Forwarded-For, X-Forwarded-Proto,
// X-Forwarded-Host to upstream requests automatically (verify: Yarp.ReverseProxy
// src/ReverseProxy/Transforms/Builder/ForwardedTransformFactory.cs — enabled by default).
// Authorization and X-Tenant-Id are passthrough headers (YARP does not strip them).
// Services validate RS256 tokens via Identity JWKS; the gateway never re-issues tokens.
app.MapReverseProxy();

app.Run();
