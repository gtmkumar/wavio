using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ServiceDiscovery;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

// Adds common Aspire services: service discovery, resilience, health checks, and OpenTelemetry.
// This project should be referenced by each service project in your solution.
// To learn more about using this project, see https://aka.ms/aspire/service-defaults
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Turn on resilience by default
            http.AddStandardResilienceHandler();

            // Turn on service discovery by default
            http.AddServiceDiscovery();
        });

        // Uncomment the following to restrict the allowed schemes for service discovery.
        // builder.Services.Configure<ServiceDiscoveryOptions>(options =>
        // {
        //     options.AllowedSchemes = ["https"];
        // });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(tracing =>
                        // Exclude health check requests from tracing
                        tracing.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath)
                    )
                    // Uncomment the following line to enable gRPC instrumentation (requires the OpenTelemetry.Instrumentation.GrpcNetClient package)
                    //.AddGrpcClientInstrumentation()
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        // Uncomment the following lines to enable the Azure Monitor exporter (requires the Azure.Monitor.OpenTelemetry.AspNetCore package)
        //if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        //{
        //    builder.Services.AddOpenTelemetry()
        //       .UseAzureMonitor();
        //}

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            // Add a default liveness check to ensure app is responsive
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Adding health checks endpoints to applications in non-development environments has security implications.
        // See https://aka.ms/aspire/healthchecks for details before enabling these endpoints in non-development environments.
        if (app.Environment.IsDevelopment())
        {
            // All health checks must pass for app to be considered ready to accept traffic after starting
            app.MapHealthChecks(HealthEndpointPath);

            // Only health checks tagged with the "live" tag must pass for app to be considered alive
            app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live")
            });
        }

        return app;
    }

    /// <summary>
    /// Adds standard security response headers to every response.
    /// No-op in Development so local tooling (Swagger UI, hot reload, etc.) is unaffected.
    /// Call early in the pipeline — before CORS / rate limiting / endpoints — so the
    /// headers are present even on preflight rejections and error responses.
    /// </summary>
    public static WebApplication UseSecurityHeaders(this WebApplication app)
    {
        // No-op in Development — keep local tooling unaffected.
        if (app.Environment.IsDevelopment())
            return app;

        app.Use(async (ctx, next) =>
        {
            var headers = ctx.Response.Headers;

            // HSTS: force HTTPS for 1 year; applies to all sub-domains.
            headers.StrictTransportSecurity = "max-age=31536000; includeSubDomains";

            // Prevent MIME-type sniffing of API responses.
            headers.XContentTypeOptions = "nosniff";

            // Block framing — APIs have no legitimate embedding use-case.
            headers.XFrameOptions = "DENY";

            // Limit Referer leakage on cross-origin requests.
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

            await next(ctx);
        });

        return app;
    }

    /// <summary>
    /// Conditionally applies <see cref="ForwardedHeadersMiddleware"/> based on the
    /// <c>ForwardedHeaders:Enabled</c> configuration flag.
    ///
    /// <para>
    /// When <c>ForwardedHeaders:Enabled = true</c> the middleware rewrites
    /// <c>HttpContext.Connection.RemoteIpAddress</c> from the socket IP to the real
    /// client IP carried in <c>X-Forwarded-For</c>, and <c>Request.Scheme</c> from the
    /// proxy-to-service transport to the value in <c>X-Forwarded-Proto</c>.  This is
    /// required for IP-based rate limiting and redirect-URI construction to behave
    /// correctly behind a load balancer or reverse proxy.
    /// </para>
    ///
    /// <para>
    /// Security note — <c>KnownIPNetworks</c> and <c>KnownProxies</c> are intentionally
    /// cleared only when the flag is set, so the middleware trusts the <em>first</em>
    /// hop in <c>X-Forwarded-For</c> completely.  This is correct when your edge
    /// proxy rewrites / sanitises the header before forwarding (e.g. AWS ALB, Azure
    /// Application Gateway, nginx with <c>proxy_set_header X-Forwarded-For</c> only).
    /// Do NOT enable this flag if the service is directly internet-exposed without a
    /// trusted proxy — an attacker could spoof the header and bypass IP rate limiting.
    /// </para>
    ///
    /// <para>
    /// Default: <c>false</c> (off in Development — loopback traffic needs no rewrite).
    /// Enable in Production/Staging by setting <c>ForwardedHeaders__Enabled=true</c>.
    /// </para>
    /// </summary>
    public static WebApplication UseForwardedHeadersIfEnabled(this WebApplication app)
    {
        var enabled = app.Configuration.GetValue<bool>("ForwardedHeaders:Enabled");
        if (!enabled)
            return app;

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        };

        // Trust the entire X-Forwarded-For chain supplied by the proxy.
        // Only safe when the edge proxy sanitises the header (see XML doc above).
        // KnownIPNetworks / KnownProxies replace the deprecated KnownNetworks / KnownProxies in .NET 10.
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();

        app.UseForwardedHeaders(options);

        return app;
    }
}
