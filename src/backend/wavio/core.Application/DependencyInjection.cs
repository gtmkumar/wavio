using System.Reflection;
using FluentValidation;
using Wavio.Utilities.CQRS.Abstractions;
using Wavio.Utilities.CQRS.Behaviors;
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

        // The only pipeline behavior activated so far: commands marked IUnitOfWorkCommand
        // (e.g. InviteUser = create user + grant membership) run in one transaction; everything
        // else passes through untouched. Requires the base DbContext alias registered in
        // core.Infrastructure. Other behaviors (logging, caching, ...) stay opt-in.
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        return services;
    }
}
