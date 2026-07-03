using System.Reflection;
using FluentValidation;
using Wavio.Utilities.CQRS.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace WaAdmin.Application;

/// <summary>
/// DI registration for the wa-admin-svc Application layer. Registers the custom CQRS dispatcher +
/// all ICommandHandler/IQueryHandler implementations (via AddCustomCQRS) and FluentValidation
/// validators. Mirrors core.Application. No mediator — handlers are dispatched directly (ADR-007).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddWaAdminApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddCustomCQRS(assembly);
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        return services;
    }
}
