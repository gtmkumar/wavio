using System.Reflection;
using FluentValidation;
using WaAdmin.Application.Templates;
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

        // Not an ICommandHandler/IQueryHandler, so AddCustomCQRS's assembly scan doesn't pick it
        // up — shared by CreateTemplateCommandHandler and SubmitTemplateCommandHandler (issue #16).
        services.AddScoped<ITemplateSubmissionService, TemplateSubmissionService>();

        return services;
    }
}
