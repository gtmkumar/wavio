using wavio.SharedDataModel.Contracts;
using wavio.SharedDataModel.Crypto;
using wavio.SharedDataModel.Persistence;
using wavio.SharedDataModel.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace wavio.SharedDataModel;

/// <summary>
/// DI registration for the shared data model library.
/// Call from your service's Program.cs:
///   builder.Services.AddSharedDataModel(connectionString);
/// An implementation of ICurrentTenant must be registered separately in the consuming service.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers <see cref="WavioDbContext"/> with Npgsql + NetTopologySuite,
    /// the <see cref="RlsConnectionInterceptor"/>, and the PII field cipher.
    ///
    /// PII encryption key (Pii:EncryptionKey):
    ///   - Development: if absent, a one-time key is auto-generated and persisted to
    ///     &lt;BaseDir&gt;/keys/dev-pii-key.b64 so it is stable across restarts.
    ///   - Non-Development: FAIL CLOSED — startup throws if the key is missing.
    ///     Provide via env var Pii__EncryptionKey or a secrets provider.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <param name="connectionString">PostgreSQL connection string.</param>
    /// <param name="configuration">Application configuration for reading Pii:EncryptionKey.</param>
    /// <param name="environment">Host environment used for the dev-key fallback guard.</param>
    public static IServiceCollection AddSharedDataModel(
        this IServiceCollection services,
        string connectionString,
        IConfiguration? configuration = null,
        IHostEnvironment? environment = null)
    {
        // ── PII field cipher ──────────────────────────────────────────────────────
        ConfigurePiiCipher(configuration, environment);

        // Expose IFieldCipher as a DI singleton so application services (e.g. settings
        // commands that encrypt/decrypt gateway secrets) can inject it without coupling
        // to the PiiValueConverter static or duplicating cipher construction.
        services.AddSingleton<IFieldCipher>(_ => PiiValueConverter.GetCipher());

        // LIFETIME RATIONALE (H1 security fix):
        // RlsConnectionInterceptor must be Scoped (per-request) for two reasons:
        //   1. It captures ICurrentTenant, which is itself Scoped (backed by HttpContext).
        //      A Singleton interceptor would hold a reference to the *first* tenant it resolved
        //      and bleed that tenant's brand_id/user_id into every subsequent request — a
        //      critical cross-tenant data leak under PostgreSQL RLS.
        //   2. Transient doesn't help: EF Core's internal service provider resolves the
        //      interceptor once when building the DbContext options snapshot and caches it
        //      within the context lifetime. Only a Scoped registration paired with
        //      AddDbContext((sp, opts) => ...) guarantees a fresh interceptor (and therefore
        //      a fresh ICurrentTenant snapshot) per DI scope (= per HTTP request).
        //
        // set_config('app.*', value, false) deliberately uses session-level (is_local=false):
        // Npgsql resets connection state on pool return, and ConnectionOpened fires on every
        // logical open, so the session var is always set to the current request's tenant before
        // any SQL executes. No leakage across pooled connections.
        services.AddScoped<RlsConnectionInterceptor>();

        // Live-revocation token-version guard (used by TenantResolutionMiddleware when
        // Auth:EnforceTokenVersion is on). Scoped: reads through the per-request DbContext.
        services.AddMemoryCache();
        services.AddScoped<wavio.SharedDataModel.Contracts.ITokenVersionStore,
            wavio.SharedDataModel.Persistence.TokenVersionStore>();

        services.AddDbContext<WavioDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.UseNetTopologySuite();
                npgsql.EnableRetryOnFailure(maxRetryCount: 3);
            });

            // Resolve the Scoped interceptor from the request-scoped IServiceProvider so
            // each request gets its own instance carrying its own ICurrentTenant.
            options.AddInterceptors(sp.GetRequiredService<RlsConnectionInterceptor>());

            // Audit-trail SaveChanges interceptor(s), registered by the host via AddAuditTrail()
            // (they live in wavio.Utilities so they can inject ICurrentUser). Enumerated as the
            // EF abstraction so SharedDataModel need not depend on Utilities. Empty when AddAuditTrail
            // was not called (e.g. integration-test setups) — the RLS interceptor is unaffected as it
            // is a DbConnectionInterceptor, not an ISaveChangesInterceptor.
            foreach (var saveInterceptor in sp.GetServices<Microsoft.EntityFrameworkCore.Diagnostics.ISaveChangesInterceptor>())
                options.AddInterceptors(saveInterceptor);
        });

        return services;
    }

    // ── PII cipher bootstrap ──────────────────────────────────────────────────
    private static void ConfigurePiiCipher(IConfiguration? config, IHostEnvironment? env)
    {
        // Already configured by a previous call (e.g. integration test setup that calls
        // AddSharedDataModel twice). PiiValueConverter.Configure is idempotent for same instance.
        if (PiiValueConverter.Instance is not null) return;

        const string ConfigKey = "Pii:EncryptionKey";
        const int KeySizeBytes = 32;

        var keyBase64 = config?[ConfigKey];

        if (string.IsNullOrWhiteSpace(keyBase64))
        {
            if (env is not null && !env.IsDevelopment())
            {
                throw new InvalidOperationException(
                    $"{ConfigKey} is required outside Development. " +
                    "Provide a 32-byte base64-encoded key via environment variable Pii__EncryptionKey " +
                    "or a secrets provider. Wavio will NOT start without it.");
            }

            // Development: auto-generate a stable per-machine key and persist it to disk.
            var keyPath = Path.Combine(AppContext.BaseDirectory, "keys", "dev-pii-key.b64");
            Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);

            byte[] keyBytes;
            if (File.Exists(keyPath))
            {
                keyBytes = Convert.FromBase64String(File.ReadAllText(keyPath).Trim());
            }
            else
            {
                keyBytes = new byte[KeySizeBytes];
                System.Security.Cryptography.RandomNumberGenerator.Fill(keyBytes);
                File.WriteAllText(keyPath, Convert.ToBase64String(keyBytes));
            }

            PiiValueConverter.Configure(new AesGcmFieldCipher(keyBytes));
        }
        else
        {
            byte[] keyBytes;
            try
            {
                keyBytes = Convert.FromBase64String(keyBase64);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException(
                    $"{ConfigKey} is not valid base64. Provide a base64-encoded 32-byte key.", ex);
            }

            PiiValueConverter.Configure(new AesGcmFieldCipher(keyBytes));
        }
    }
}
