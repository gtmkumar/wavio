using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace WaAdmin.Infrastructure;

/// <summary>
/// DI registration for the wa-admin-svc Infrastructure layer.
/// WABA/phone onboarding (Embedded Signup), template lifecycle, business profile, rate-card sync (Wave 1, #16)
/// Register the service's data-access surface (IWaAdminDbContext over the shared context),
/// Meta Graph API clients, and bus publishers here as the wave issues land.
/// Call from the host: <c>builder.Services.AddWaAdminInfrastructure(builder.Configuration);</c>
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddWaAdminInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Scaffold (#8): no registrations yet — wave issues add the data surface,
        // Graph API clients, and RabbitMQ publishers/consumers per service.
        return services;
    }
}
