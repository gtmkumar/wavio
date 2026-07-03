using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace WaIngest.Infrastructure;

/// <summary>
/// DI registration for the wa-ingest-svc Infrastructure layer.
/// Meta webhook receiver: X-Hub-Signature-256 verify, raw persist, wamid dedupe, normalize, publish to bus (Wave 1, #13)
/// Register the service's data-access surface (IWaIngestDbContext over the shared context),
/// Meta Graph API clients, and bus publishers here as the wave issues land.
/// Call from the host: <c>builder.Services.AddWaIngestInfrastructure(builder.Configuration);</c>
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddWaIngestInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Scaffold (#8): no registrations yet — wave issues add the data surface,
        // Graph API clients, and RabbitMQ publishers/consumers per service.
        return services;
    }
}
