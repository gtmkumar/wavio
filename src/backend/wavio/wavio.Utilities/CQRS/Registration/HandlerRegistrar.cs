using System.Reflection;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Wavio.Utilities.CQRS.Registration;

/// <summary>
/// Scans assemblies for closed <see cref="ICommandHandler{TCommand,TResult}"/> and
/// <see cref="IQueryHandler{TQuery,TResult}"/> implementations and registers them as scoped services.
/// </summary>
public static class HandlerRegistrar
{
    public static IServiceCollection RegisterHandlers(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            services.Scan(scan => scan
                .FromAssemblies(assembly)
                .AddClasses(classes =>
                    classes.AssignableTo(typeof(ICommandHandler<,>)))
                .AsImplementedInterfaces()
                .WithScopedLifetime());

            services.Scan(scan => scan
                .FromAssemblies(assembly)
                .AddClasses(classes =>
                    classes.AssignableTo(typeof(IQueryHandler<,>)))
                .AsImplementedInterfaces()
                .WithScopedLifetime());
        }

        return services;
    }
}
