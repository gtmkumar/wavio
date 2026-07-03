using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace WaGateway.Infrastructure;

/// <summary>
/// DI registration for the wa-gateway-svc Infrastructure layer.
/// Outbound send API: messages, media, interactive, Flows launch; idempotency, outbox, retries, rate limiting (Wave 1, #14)
/// Register the service's data-access surface (IWaGatewayDbContext over the shared context),
/// Meta Graph API clients, and bus publishers here as the wave issues land.
/// Call from the host: <c>builder.Services.AddWaGatewayInfrastructure(builder.Configuration);</c>
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddWaGatewayInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Scaffold (#8): no registrations yet — wave issues add the data surface,
        // Graph API clients, and RabbitMQ publishers/consumers per service.
        return services;
    }
}
