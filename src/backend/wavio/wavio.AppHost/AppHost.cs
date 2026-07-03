// Wavio — Aspire AppHost (LOCAL DEV ONLY; the VPS runs docker-compose — see docs/BUILD_PLAN.md)
// Single entry-point that starts identity + the 5 wa-* platform services + gateway
// under the Aspire dashboard (spec §3.3).
//
//   core       @5050 = Identity (auth/users/roles) — see core.WebApi
//   wa-gateway @5101 = outbound send API (messages, media, interactive, Flows launch)
//   wa-ingest  @5102 = Meta webhook receiver (verify, dedupe, normalize, publish)
//   wa-admin   @5103 = WABA/phone onboarding, template lifecycle, rate-card sync
//   wa-billing @5104 = PMP cost ledger, metering, quotas, invoicing feed
//   wa-intel   @5105 = Quality Guardian, session windows, analytics, AI gateway
//   gateway    @8080 = single YARP entry-point for all clients
//
// Usage:
//   ASPNETCORE_ENVIRONMENT=Development dotnet run --project wavio.AppHost
//
// Dashboard: the URL (with login token) is printed to stdout on startup.
//
// Postgres + RabbitMQ: NO containers are spun up here — start them via the dev
// docker-compose (issue #9). This AppHost only injects the connection config:
//   ConnectionStrings__Default = Postgres `waplatform` DB (non-superuser, RLS enforced)
//   ConnectionStrings__RabbitMq = amqp broker for integration events / outbox relay

var builder = DistributedApplication.CreateBuilder(args);

// ── Resolve shared connection strings from AppHost config ───────────────────────────────
// Source priority: env var > appsettings.Development.json > appsettings.json > literal fallback.
// Default = non-superuser app_user (RLS ENFORCED at runtime).
var connStr = builder.Configuration["ConnectionStrings:Default"]
    ?? "Host=localhost;Port=5432;Database=waplatform;Username=app_user;Password=app_user";

// Admin = postgres/superuser, injected for Development seeding only (bypasses RLS natively).
var adminConnStr = builder.Configuration["ConnectionStrings:Admin"]
    ?? "Host=localhost;Port=5432;Database=waplatform;Username=postgres;Password=postgres";

// RabbitMQ (integration events, outbox relay — spec §3.2). Broker comes from the dev
// docker-compose (issue #9); default matches its local guest credentials.
var rabbitConnStr = builder.Configuration["ConnectionStrings:RabbitMq"]
    ?? "amqp://guest:guest@localhost:5672";

// ── Shared dev PII key ──────────────────────────────────────────────────────────────────
// AppHost loads (or generates once) a single 32-byte key in its own keys/ directory and
// injects Pii__EncryptionKey into ALL services so the per-service auto-gen never diverges
// (one service encrypting with key-A while another decrypts with key-B).
// The key file is gitignored via **/keys/*.b64.
// Production: provide Pii__EncryptionKey via SOPS/age-managed env — the key file is never read.
var devPiiKeyBase64 = LoadOrGenerateDevPiiKey();

// ── Services — ports are FIXED (gateway clusters + appsettings hard-reference them) ─────

// core = Identity (auth/users/roles/permissions). Issues the RS256 JWTs every other
// service validates via its JWKS endpoint.
builder
    .AddProject<Projects.core_WebApi>("core")
    .WithHttpEndpoint(port: 5050, name: "http")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("ConnectionStrings__Default", connStr)
    .WithEnvironment("ConnectionStrings__Admin", adminConnStr)
    .WithEnvironment("Pii__EncryptionKey", devPiiKeyBase64);

// The 5 wa-* platform services (spec §3.3). Jwt__Authority overrides appsettings so that
// under the AppHost, JWKS validation points at core's fixed port 5050.
void AddPlatformService<TProject>(string name, int port)
    where TProject : IProjectMetadata, new()
{
    builder
        .AddProject<TProject>(name)
        .WithHttpEndpoint(port: port, name: "http")
        .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
        .WithEnvironment("ConnectionStrings__Default", connStr)
        .WithEnvironment("ConnectionStrings__Admin", adminConnStr)
        .WithEnvironment("ConnectionStrings__RabbitMq", rabbitConnStr)
        .WithEnvironment("Jwt__Authority", "http://localhost:5050")
        .WithEnvironment("Pii__EncryptionKey", devPiiKeyBase64);
}

AddPlatformService<Projects.WaGateway_WebApi>("wa-gateway-svc", 5101);
AddPlatformService<Projects.WaIngest_WebApi>("wa-ingest-svc", 5102);
AddPlatformService<Projects.WaAdmin_WebApi>("wa-admin-svc", 5103);
AddPlatformService<Projects.WaBilling_WebApi>("wa-billing-svc", 5104);
AddPlatformService<Projects.WaIntel_WebApi>("wa-intel-svc", 5105);

// ── API Gateway — single entry-point for all clients at :8080 ───────────────────────────
// ADDITIVE: all per-service direct ports remain active.
// Downstream cluster addresses are injected via env vars so YARP uses the same
// port-fixed addresses as the other resources. No service-discovery magic — consistent
// with the "static ports" convention used throughout this AppHost.
//
// Path → cluster → service:
//   /identity   → core       @5050
//   /messaging  → wa-gateway @5101
//   /ingest     → wa-ingest  @5102
//   /admin      → wa-admin   @5103
//   /billing    → wa-billing @5104
//   /intel      → wa-intel   @5105
builder
    .AddProject<Projects.wavio_Gateway>("gateway")
    .WithHttpEndpoint(port: 8080, name: "http")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("Pii__EncryptionKey", devPiiKeyBase64)
    // Inject each downstream cluster address. Overrides appsettings.json defaults;
    // double-underscore maps to Gateway:Clusters:{name}:Destinations:primary:Address.
    .WithEnvironment("Gateway__Clusters__identity__Destinations__primary__Address",     "http://localhost:5050")
    .WithEnvironment("Gateway__Clusters__wa-gateway__Destinations__primary__Address",   "http://localhost:5101")
    .WithEnvironment("Gateway__Clusters__wa-ingest__Destinations__primary__Address",    "http://localhost:5102")
    .WithEnvironment("Gateway__Clusters__wa-admin__Destinations__primary__Address",     "http://localhost:5103")
    .WithEnvironment("Gateway__Clusters__wa-billing__Destinations__primary__Address",   "http://localhost:5104")
    .WithEnvironment("Gateway__Clusters__wa-intel__Destinations__primary__Address",     "http://localhost:5105");

builder.Build().Run();

// ── Dev PII key bootstrap ─────────────────────────────────────────────────────────────
// Loads the shared dev key from the AppHost keys/ directory, generating it on first run.
// This file is gitignored — each dev machine has its own persistent key.
// If the key file is deleted all enc:v1 values in the dev DB become unreadable; the
// defensive catch in FromJson degrades to disabled defaults and a fresh key is generated
// on the next AppHost start.  Simply re-save any integration settings through the admin
// panel to re-encrypt with the new key.
static string LoadOrGenerateDevPiiKey()
{
    // Store the key next to the AppHost build output, so it is the same path regardless
    // of build configuration and survives between runs.
    var keyDir = Path.Combine(AppContext.BaseDirectory, "keys");
    var keyPath = Path.Combine(keyDir, "dev-pii-key.b64");

    Directory.CreateDirectory(keyDir);

    if (File.Exists(keyPath))
    {
        var existing = File.ReadAllText(keyPath).Trim();
        // Validate: must be 32 bytes when decoded.
        if (Convert.FromBase64String(existing).Length == 32)
            return existing;

        // File is corrupt — regenerate below.
        Console.WriteLine("[AppHost] WARNING: dev-pii-key.b64 was invalid; regenerating. " +
                          "Existing enc:v1 values in the dev DB will be unreadable until re-saved.");
    }
    else
    {
        Console.WriteLine("[AppHost] Generating new shared dev PII key → " + keyPath);
    }

    var keyBytes = new byte[32];
    System.Security.Cryptography.RandomNumberGenerator.Fill(keyBytes);
    var keyBase64 = Convert.ToBase64String(keyBytes);
    File.WriteAllText(keyPath, keyBase64);
    return keyBase64;
}
