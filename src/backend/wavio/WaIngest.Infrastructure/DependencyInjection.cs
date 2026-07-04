using WaIngest.Application.Common.Interfaces;
using WaIngest.Infrastructure.BackgroundWork;
using WaIngest.Infrastructure.Messaging;
using WaIngest.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace WaIngest.Infrastructure;

/// <summary>
/// DI registration for the wa-ingest-svc Infrastructure layer (issue #13: Meta webhook receiver —
/// verify, raw persist, wamid dedupe, normalize, publish to bus).
/// Call from the host: <c>builder.Services.AddWaIngestInfrastructure(builder.Configuration);</c>
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddWaIngestInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Data-access surface over the shared WavioDbContext (ingest.raw_webhooks / webhook_dedupe).
        services.AddScoped<IWaIngestDbContext, WaIngestDbContext>();

        // RabbitMQ: one connection per host (Singleton manager, reconnects lazily on failure);
        // the publisher is stateless besides that, so it is Singleton too — Scoped consumers
        // (WebhookProcessor) may safely depend on a Singleton.
        services.AddSingleton<RabbitMqConnectionManager>();
        services.AddSingleton<IEventBusPublisher, RabbitMqEventBusPublisher>();

        // Background worker: drains the in-process queue (WaIngest.Application) and runs
        // dedupe/normalize/publish off the HTTP ack path. Also performs crash-recovery on startup.
        services.AddHostedService<WebhookIngestBackgroundService>();

        return services;
    }
}
