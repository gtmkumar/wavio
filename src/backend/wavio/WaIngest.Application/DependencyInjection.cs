using System.Reflection;
using FluentValidation;
using WaIngest.Application.Ingestion;
using Wavio.Utilities.CQRS.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace WaIngest.Application;

/// <summary>
/// DI registration for the wa-ingest-svc Application layer. Registers the custom CQRS dispatcher +
/// all ICommandHandler/IQueryHandler implementations (via AddCustomCQRS) and FluentValidation
/// validators. Mirrors core.Application. No mediator — handlers are dispatched directly (ADR-007).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddWaIngestApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddCustomCQRS(assembly);
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        // In-process hand-off buffer: one per host instance, shared by every request and by the
        // single background worker (registered in WaIngest.Infrastructure).
        services.AddSingleton<IWebhookIngestBuffer, WebhookIngestBuffer>();

        // Dedupe/normalize/publish orchestrator — Scoped because it depends on IWaIngestDbContext
        // (itself Scoped over the shared WavioDbContext). Invoked from a fresh DI scope per queue
        // item by the background worker, and per replay request by ReplayWebhooksHandler.
        services.AddScoped<IWebhookProcessor, WebhookProcessor>();

        return services;
    }
}
