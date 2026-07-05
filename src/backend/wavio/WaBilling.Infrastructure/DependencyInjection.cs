using WaBilling.Application.Common.Interfaces;
using WaBilling.Infrastructure.Messaging;
using WaBilling.Infrastructure.Persistence;
using WaBilling.Infrastructure.TenantResolution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace WaBilling.Infrastructure;

/// <summary>
/// DI registration for the wa-billing-svc Infrastructure layer.
/// PMP cost ledger, tenant metering, quotas, invoicing feed (issue #19): data surface, tenant
/// resolver, and the wa.message.status.v1 -&gt; cost-ledger RabbitMQ consumer.
/// Call from the host: <c>builder.Services.AddWaBillingInfrastructure(builder.Configuration);</c>
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddWaBillingInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IWaBillingDbContext, WaBillingDbContext>();
        services.AddScoped<ITenantResolver, WabaPhoneNumberTenantResolver>();

        services.AddSingleton<RabbitMqConnectionManager>();
        services.AddHostedService<MessageStatusConsumerService>();

        return services;
    }
}
