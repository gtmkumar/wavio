using System.Reflection;
using Wavio.Utilities.CQRS.Abstractions;
using Wavio.Utilities.CQRS.Dispatcher;
using Microsoft.Extensions.DependencyInjection;

namespace Wavio.Utilities.CQRS.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCustomCQRS(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        services.AddScoped<IDispatcher, Dispatcher.Dispatcher>();

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
