using WaGateway.Application.Common.Interfaces;
using WaGateway.Infrastructure.BackgroundWork;
using WaGateway.Infrastructure.Graph;
using WaGateway.Infrastructure.Messaging;
using WaGateway.Infrastructure.Persistence;
using WaGateway.Infrastructure.RateLimiting;
using WaGateway.Infrastructure.WindowState;
using wavio.SharedDataModel.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace WaGateway.Infrastructure;

/// <summary>
/// DI registration for the wa-gateway-svc Infrastructure layer — outbound send API (issue #14):
/// data surface, RabbitMQ publisher, Meta Graph client, window-state client, rate limiters, and
/// the outbox dispatcher.
/// Call from the host: <c>builder.Services.AddWaGatewayInfrastructure(builder.Configuration);</c>
/// The host must ALSO call <c>services.Replace(ServiceDescriptor.Scoped&lt;ICurrentTenant,
/// ScopedCurrentTenant&gt;())</c> after <c>AddCurrentTenant()</c> — see Program.cs.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddWaGatewayInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddMemoryCache();

        services.AddScoped<IWaGatewayDbContext, WaGatewayDbContext>();

        services.AddSingleton<RabbitMqConnectionManager>();
        services.AddSingleton<IEventBusPublisher, RabbitMqEventBusPublisher>();

        services.AddSingleton<TokenBucketRateLimiter>();
        services.AddSingleton<MessagingTierGate>();
        services.AddSingleton<GuardianThrottleGate>();

        services.Configure<MetaGraphOptions>(configuration.GetSection(MetaGraphOptions.SectionName));

        // Startup sanity check (security review, PR #45, S1): the Graph client's Timeout MUST be
        // strictly less than the stale-lock reclaim window, or a slow Graph response can still be
        // in flight when its lease is reclaimed and re-sent — see MetaGraphOptions.TimeoutSeconds'
        // doc comment and OutboxDispatcherService's fenced-write comments for the full mechanism.
        // Runs eagerly here (not lazily inside the HttpClient configuration callback, which only
        // runs on first use) so a misconfiguration fails at boot, not on the first slow send.
        var staleLockSeconds = configuration.GetValue("Outbox:StaleLockSeconds", 30);
        var configuredTimeoutSeconds = configuration.GetValue("Meta:Graph:TimeoutSeconds", 0);
        var graphTimeoutSeconds = configuredTimeoutSeconds > 0
            ? configuredTimeoutSeconds
            : Math.Max(5, staleLockSeconds - 5); // default: 5s of headroom below the reclaim window

        if (graphTimeoutSeconds >= staleLockSeconds)
        {
            throw new InvalidOperationException(
                $"Meta:Graph:TimeoutSeconds ({graphTimeoutSeconds}s) must be strictly less than " +
                $"Outbox:StaleLockSeconds ({staleLockSeconds}s) — otherwise a slow Graph response can " +
                "still be in flight when its outbox lease is reclaimed as stale, and a duplicate send " +
                "results if both the original and the reclaimed attempt succeed (security review, PR #45, " +
                "S1). Configure Meta:Graph:TimeoutSeconds explicitly with headroom, or raise " +
                "Outbox:StaleLockSeconds.");
        }

        var graphClientBuilder = services.AddHttpClient<IMetaGraphMessageClient, MetaGraphMessageClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<MetaGraphOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                client.BaseAddress = new Uri(options.BaseUrl);
            }
            client.Timeout = TimeSpan.FromSeconds(graphTimeoutSeconds);
        });

        // Aspire's AddServiceDefaults() applies a standard Polly resilience handler (its own
        // internal retry-with-backoff) to every typed HttpClient by default. The outbox
        // dispatcher already implements its own deliberate, durable, observable retry policy
        // (GraphErrorClassifier + attempts/backoff on outbound_outbox) — leaving Polly's handler
        // in place would silently retry INSIDE a single "attempt" a few more times, desyncing the
        // attempts column from the real number of Graph HTTP calls (found live: a stub 500 was
        // actually called 3x per attempt before this fix). Removed here, not globally, so other
        // clients keep the platform default. RemoveAllResilienceHandlers is marked experimental
        // (EXTEXP0001) by Microsoft.Extensions.Http.Resilience but is exactly the sanctioned way
        // to opt a specific client out of ConfigureHttpClientDefaults' handler; suppressed
        // deliberately, not blanket-disabled for the project.
#pragma warning disable EXTEXP0001
        graphClientBuilder.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

        services.AddHttpClient<IWindowStateClient, HttpWindowStateClient>(client =>
        {
            var baseUrl = configuration["Services:WaIntel:BaseUrl"] ?? "http://localhost:5105";
            client.BaseAddress = new Uri(baseUrl);
        });

        services.AddHostedService<OutboxDispatcherService>();

        return services;
    }

    /// <summary>
    /// Swaps the shared <c>HttpContextCurrentTenant</c> (registered by
    /// <c>services.AddCurrentTenant()</c>) for <see cref="ScopedCurrentTenant"/>, which the
    /// outbox dispatcher needs so RLS-scoped queries work from a background scope with no
    /// HttpContext. Call AFTER <c>AddCurrentTenant()</c> in Program.cs. Kept as a separate,
    /// explicitly-named method rather than folded into <see cref="AddWaGatewayInfrastructure"/>
    /// so the override is visible at the call site, not buried inside a generic "add
    /// infrastructure" call.
    /// </summary>
    public static IServiceCollection ReplaceCurrentTenantWithScopedVersion(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Scoped<ICurrentTenant, ScopedCurrentTenant>());
        return services;
    }
}
