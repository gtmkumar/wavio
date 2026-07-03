using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace WaIntel.Infrastructure;

/// <summary>
/// DI registration for the wa-intel-svc Infrastructure layer.
/// Quality Rating Guardian, session window state, analytics event store, AI orchestration gateway (Waves 1-4)
/// Register the service's data-access surface (IWaIntelDbContext over the shared context),
/// Meta Graph API clients, and bus publishers here as the wave issues land.
/// Call from the host: <c>builder.Services.AddWaIntelInfrastructure(builder.Configuration);</c>
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddWaIntelInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Scaffold (#8): no registrations yet — wave issues add the data surface,
        // Graph API clients, and RabbitMQ publishers/consumers per service.
        return services;
    }
}
