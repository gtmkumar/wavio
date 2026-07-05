using Microsoft.Extensions.DependencyInjection;
using wavio.SharedDataModel;
using wavio.SharedDataModel.Contracts;
using WaAdmin.Application.Common.Interfaces;
using WaAdmin.Infrastructure.Persistence;
using WaBilling.Application.Common.Interfaces;
using WaBilling.Infrastructure.Persistence;
using WaGateway.Application.Common.Interfaces;
using WaGateway.Infrastructure.Persistence;

namespace WaPlatform.IntegrationTests.Support;

/// <summary>
/// Wires the exact same production DI shape each service's own DependencyInjection.cs uses
/// (<c>AddSharedDataModel</c> -&gt; scoped <c>WavioDbContext</c> + <c>RlsConnectionInterceptor</c>,
/// then the service-specific IWa*DbContext adapter) — just assembled directly here instead of
/// through a WebApi host's Program.cs, since these tests drive Application-layer handlers/services
/// directly (same convention as every existing *.Tests project). One <see cref="TestCurrentTenant"/>
/// singleton per provider: mutate its <c>TenantId</c> between <c>CreateScope()</c> calls to switch
/// which tenant a given scope's connection opens as — safe because
/// <c>RlsConnectionInterceptor.ConnectionOpened</c> re-reads it on every logical connection open
/// (see wavio.SharedDataModel/DependencyInjection.cs's own doc comment on why the interceptor
/// itself must stay Scoped even though the ICurrentTenant instance here does not vary per-scope).
/// </summary>
public static class TestHost
{
    /// <summary>DB access wired exactly like WaGateway.WebApi's Program.cs: AddSharedDataModel +
    /// the real <see cref="ScopedCurrentTenant"/> (background-dispatcher-capable), needed because
    /// <see cref="WaGateway.Infrastructure.BackgroundWork.OutboxDispatcherService"/> itself casts
    /// to <see cref="ScopedCurrentTenant"/> and sets <c>OverrideTenantId</c> per outbox entry.</summary>
    public static ServiceProvider BuildGatewayProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddHttpContextAccessor();
        services.AddSharedDataModel(connectionString);
        services.AddScoped<ICurrentTenant, ScopedCurrentTenant>();
        services.AddScoped<IWaGatewayDbContext, WaGatewayDbContext>();
        return services.BuildServiceProvider();
    }

    /// <summary>DB access for a plain tenant-scoped handler test (WaAdmin) — the caller drives the
    /// tenant via the returned <see cref="TestCurrentTenant"/>'s mutable <c>TenantId</c> before
    /// opening each scope (no HTTP request/JWT in these tests, so ScopedCurrentTenant's claim-based
    /// resolution does not apply here).</summary>
    public static (ServiceProvider Provider, TestCurrentTenant CurrentTenant) BuildAdminProvider(string connectionString)
    {
        var currentTenant = new TestCurrentTenant();
        var services = new ServiceCollection();
        services.AddSharedDataModel(connectionString);
        services.AddSingleton<ICurrentTenant>(currentTenant);
        services.AddScoped<IWaAdminDbContext, WaAdminDbContext>();
        return (services.BuildServiceProvider(), currentTenant);
    }

    public static (ServiceProvider Provider, TestCurrentTenant CurrentTenant) BuildBillingProvider(string connectionString)
    {
        var currentTenant = new TestCurrentTenant();
        var services = new ServiceCollection();
        services.AddSharedDataModel(connectionString);
        services.AddSingleton<ICurrentTenant>(currentTenant);
        services.AddScoped<IWaBillingDbContext, WaBillingDbContext>();
        return (services.BuildServiceProvider(), currentTenant);
    }

    /// <summary>Raw <see cref="IWaGatewayDbContext"/> access with an explicit, externally-driven
    /// tenant (RLS isolation test) — same shape as <see cref="BuildAdminProvider"/> but exposing
    /// the Gateway data surface instead.</summary>
    public static (ServiceProvider Provider, TestCurrentTenant CurrentTenant) BuildGatewayProviderWithTestTenant(string connectionString)
    {
        var currentTenant = new TestCurrentTenant();
        var services = new ServiceCollection();
        services.AddHttpContextAccessor();
        services.AddSharedDataModel(connectionString);
        services.AddSingleton<ICurrentTenant>(currentTenant);
        services.AddScoped<IWaGatewayDbContext, WaGatewayDbContext>();
        return (services.BuildServiceProvider(), currentTenant);
    }
}
