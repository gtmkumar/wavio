namespace wavio.Gateway;

/// <summary>
/// Maps GET /health/services — fans out to each downstream service's /health/ready
/// endpoint in parallel (3 s timeout each, configured on the named HttpClient),
/// and returns a JSON summary plus an overall status code:
///   200 if ALL services are healthy, 207 Multi-Status if any are degraded/unhealthy.
///
/// Response shape:
/// <code>
/// {
///   "overall": "Healthy" | "Degraded",
///   "services": [
///     { "service": "identity",  "status": "Healthy" },
///     { "service": "catalog",   "status": "Unhealthy" },
///     ...
///   ]
/// }
/// </code>
///
/// This endpoint is unauthenticated (consistent with /health/ready on each service
/// which is also unauthenticated in dev — see PRODUCTION_ENV.md for prod guidance).
/// </summary>
public static class HealthServicesEndpoint
{
    /// <summary>Named HttpClient registered in Program.cs with a 3-second timeout.</summary>
    public const string HttpClientName = "HealthCheck";

    // Post-consolidation: 3 distinct hosts. The Name is also a YARP cluster id whose
    // configured address is reused for the probe (see MapHealthServicesEndpoint), so the
    // fan-out follows any per-environment cluster address override.
    //   core       (5050) = identity + engagement + mcp
    //   operations (5002) = catalog + orders + warehouse + logistics  (cluster id "orders")
    //   commerce   (5005) = commerce + finance + analytics
    private static readonly (string Name, string Path)[] ServiceHealthPaths =
    [
        ("identity", "http://localhost:5050/health/ready"),
        ("orders",   "http://localhost:5002/health/ready"),
        ("commerce", "http://localhost:5005/health/ready"),
    ];

    /// <summary>
    /// Registers the /health/services route. Reads service URLs from
    /// <c>Gateway:Clusters:{name}:Destinations:primary:Address</c> so that the
    /// fan-out uses the same configured addresses as the YARP proxy clusters.
    /// Falls back to the hard-coded dev defaults when config is absent.
    /// </summary>
    public static WebApplication MapHealthServicesEndpoint(
        this WebApplication app,
        IConfiguration configuration)
    {
        app.MapGet("/health/services", async (IHttpClientFactory factory) =>
        {
            // Build service→URL mapping from the same config that drives YARP clusters,
            // so a non-dev environment that overrides cluster addresses is also probed
            // correctly here without additional config keys.
            var targets = ServiceHealthPaths
                .Select(s =>
                {
                    var configuredBase = configuration[
                        $"Gateway:Clusters:{s.Name}:Destinations:primary:Address"];

                    var baseUrl = string.IsNullOrWhiteSpace(configuredBase)
                        ? ExtractBase(s.Path)   // fall back to the hard-coded dev default
                        : configuredBase.TrimEnd('/');

                    return (s.Name, Url: $"{baseUrl}/health");
                })
                .ToArray();

            var client = factory.CreateClient(HttpClientName);

            // Fan out in parallel — each probe has at most 3 s (set on the named client).
            var probes = targets.Select(async t =>
            {
                string status;
                try
                {
                    var response = await client.GetAsync(t.Url);
                    status = response.IsSuccessStatusCode ? "Healthy" : "Unhealthy";
                }
                catch
                {
                    status = "Unreachable";
                }
                return new { service = t.Name, status };
            });

            var results = await Task.WhenAll(probes);

            var allHealthy = results.All(r => r.status == "Healthy");
            var overall    = allHealthy ? "Healthy" : "Degraded";
            var httpStatus = allHealthy
                ? StatusCodes.Status200OK
                : StatusCodes.Status207MultiStatus;

            return Results.Json(new { overall, services = results }, statusCode: httpStatus);
        })
        .WithName("HealthServices")
        .WithTags("Health")
        .ExcludeFromDescription(); // keep out of OpenAPI docs — internal ops endpoint

        return app;
    }

    // Extracts "http://host:port" from a full URL string.
    private static string ExtractBase(string url)
    {
        var uri = new Uri(url);
        return $"{uri.Scheme}://{uri.Host}:{uri.Port}";
    }
}
