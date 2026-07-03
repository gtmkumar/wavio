using core.Application.Common.Interfaces;
using core.Infrastructure.Auth;
using core.Infrastructure.Email;
using core.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace core.Infrastructure;

/// <summary>
/// DI registration for the core Infrastructure layer. Registers the core data-access surface
/// (<see cref="ICoreDbContext"/>) over the shared context. Handlers depend on the interface; no repositories.
/// Call from the host: <c>builder.Services.AddCoreInfrastructure();</c>
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddCoreInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<ICoreDbContext, CoreDbContext>();

        // ICurrentTenant (RLS) is now a cross-cutting registration via AddCurrentTenant() in the
        // host (wavio.Utilities.Services.HttpContextCurrentTenant) — shared with Operations.

        // SMTP transport for the invite/activation email flows.
        // Reads tenant-scoped SMTP config from kernel.system_settings via ICoreDbContext.
        services.AddScoped<ISettingsMailer, SettingsMailer>();

        // Refresh-token root insert (raw SQL for the self-referential family_id FK).
        // Injects the concrete WavioDbContext for Database.ExecuteSqlAsync.
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

        return services;
    }
}
