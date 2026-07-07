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

        // TransactionBehavior (unit-of-work commands, see core.Application DI) resolves the
        // ambient context as the base DbContext type — alias it to the same scoped
        // WavioDbContext instance CoreDbContext wraps, so the transaction and the handlers'
        // SaveChanges share one connection.
        services.AddScoped<Microsoft.EntityFrameworkCore.DbContext>(
            sp => sp.GetRequiredService<wavio.SharedDataModel.Persistence.WavioDbContext>());

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
