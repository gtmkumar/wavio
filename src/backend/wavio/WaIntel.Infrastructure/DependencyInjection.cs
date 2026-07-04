using WaIntel.Application.Common.Interfaces;
using WaIntel.Infrastructure.BackgroundWork;
using WaIntel.Infrastructure.Caching;
using WaIntel.Infrastructure.Messaging;
using WaIntel.Infrastructure.Persistence;
using WaIntel.Infrastructure.TenantResolution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace WaIntel.Infrastructure;

/// <summary>
/// DI registration for the wa-intel-svc Infrastructure layer — Session Window Manager (issue
/// #15): data surface, RabbitMQ consumer/publisher, tenant resolver, fast-lookup cache +
/// LISTEN/NOTIFY invalidation, and the cross-tenant closing-window scanner.
/// Call from the host: <c>builder.Services.AddWaIntelInfrastructure(builder.Configuration);</c>
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddWaIntelInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IWaIntelDbContext, WaIntelDbContext>();

        services.AddMemoryCache();

        services.AddSingleton<RabbitMqConnectionManager>();
        services.AddSingleton<IEventBusPublisher, RabbitMqEventBusPublisher>();
        services.AddScoped<ITenantResolver, WabaPhoneNumberTenantResolver>();

        services.AddHostedService<MessageReceivedConsumerService>();
        services.AddHostedService<WindowCacheInvalidationListener>();
        services.AddHostedService<WindowClosingScannerService>();

        return services;
    }
}
