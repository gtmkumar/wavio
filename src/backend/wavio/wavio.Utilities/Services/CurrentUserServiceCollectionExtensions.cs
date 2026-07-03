using wavio.SharedDataModel.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace wavio.Utilities.Services;

public static class CurrentUserServiceCollectionExtensions
{
    /// <summary>Registers <see cref="ICurrentUser"/> backed by the HTTP request principal.</summary>
    public static IServiceCollection AddCurrentUser(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="ICurrentTenant"/> backed by HttpContext JWT claims — the tenant
    /// context the shared RLS connection interceptor reads at runtime. Cross-cutting: every
    /// bounded-context host calls this (mirror of <see cref="AddCurrentUser"/>).
    /// </summary>
    public static IServiceCollection AddCurrentTenant(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentTenant, HttpContextCurrentTenant>();
        return services;
    }
}
