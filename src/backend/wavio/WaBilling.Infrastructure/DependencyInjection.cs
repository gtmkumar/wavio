using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace WaBilling.Infrastructure;

/// <summary>
/// DI registration for the wa-billing-svc Infrastructure layer.
/// PMP cost ledger, tenant metering, quotas, invoicing feed, max-price bid config (Wave 2, #19)
/// Register the service's data-access surface (IWaBillingDbContext over the shared context),
/// Meta Graph API clients, and bus publishers here as the wave issues land.
/// Call from the host: <c>builder.Services.AddWaBillingInfrastructure(builder.Configuration);</c>
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddWaBillingInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Scaffold (#8): no registrations yet — wave issues add the data surface,
        // Graph API clients, and RabbitMQ publishers/consumers per service.
        return services;
    }
}
