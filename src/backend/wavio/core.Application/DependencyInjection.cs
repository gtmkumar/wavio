using System.Reflection;
using FluentValidation;
using Wavio.Utilities.CQRS.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace core.Application;

/// <summary>
/// DI registration for the core Application layer. Registers the custom CQRS dispatcher +
/// all ICommandHandler/IQueryHandler implementations (via AddCustomCQRS) and FluentValidation validators.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddCoreApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddCustomCQRS(assembly);
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        return services;
    }
}
