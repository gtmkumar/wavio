using WaAdmin.Application.Common.Interfaces;
using WaAdmin.Infrastructure.BackgroundWork;
using WaAdmin.Infrastructure.Graph;
using WaAdmin.Infrastructure.Messaging;
using WaAdmin.Infrastructure.Persistence;
using WaAdmin.Infrastructure.Templates;
using WaAdmin.Infrastructure.TenantResolution;
using wavio.SharedDataModel.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace WaAdmin.Infrastructure;

/// <summary>
/// DI registration for the wa-admin-svc Infrastructure layer.
/// WABA/phone onboarding (Embedded Signup), template lifecycle, business profile, rate-card sync (Wave 1, #16)
/// Register the service's data-access surface (IWaAdminDbContext over the shared context),
/// Meta Graph API clients, and bus publishers here as the wave issues land.
/// Call from the host: <c>builder.Services.AddWaAdminInfrastructure(builder.Configuration);</c>
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddWaAdminInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Data-access surface over the shared WavioDbContext (templates.* — issue #16).
        services.AddScoped<IWaAdminDbContext, WaAdminDbContext>();

        // ICurrentTenant: replace the HTTP-only default so the same registration correctly
        // serves both HTTP requests and the background template-events consumer's per-message DI
        // scope. See ScopedCurrentTenant's doc comment for why this replacement is necessary.
        services.AddScoped<ScopedCurrentTenant>();
        services.Replace(ServiceDescriptor.Scoped<ICurrentTenant>(sp => sp.GetRequiredService<ScopedCurrentTenant>()));

        // Lint stub (Wave 1) — always passes; Wave 3 (#27) adds real rules/LLM linters.
        services.AddScoped<ITemplateLintService, StubTemplateLintService>();

        // Wave 2 hooks (#19 billing, #22 campaigns) — honest no-ops for now, see their doc comments.
        services.AddScoped<ICampaignFreezeHook, NoOpCampaignFreezeHook>();
        services.AddScoped<IBillingRecalibrationHook, NoOpBillingRecalibrationHook>();
        services.AddScoped<ITenantAlertPublisher, LoggingTenantAlertPublisher>();

        // Meta Graph API client (issue #16 Task 2) — BaseAddress bound lazily from
        // MetaGraphOptions so the host can boot with no Meta:Graph:BaseUrl configured and fail
        // only when a submit is actually attempted (mirrors the rest of this host's "boot even
        // without full config" posture for optional-at-startup external dependencies).
        services.Configure<MetaGraphOptions>(configuration.GetSection(MetaGraphOptions.SectionName));
        services.AddHttpClient<IWhatsAppTemplateGraphClient, MetaGraphTemplateClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MetaGraphOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
                client.BaseAddress = new Uri(options.BaseUrl);
        });

        // RabbitMQ: one connection per host (Singleton manager, reconnects lazily on failure).
        services.AddSingleton<RabbitMqConnectionManager>();

        // Background consumer: wa.template.status_changed.v1 / wa.template.category_changed.v1
        // from the wavio.events exchange (issue #16 Tasks 3-4).
        services.AddHostedService<TemplateEventsConsumerBackgroundService>();

        // Consent ledger (issue #21, spec §4.10): STOP-keyword listener needs its own tenant
        // resolver (same "each service owns its own copy" convention as WaIntel/WaBilling,
        // issue #15) plus the erasure/export background worker. Both are wired unconditionally —
        // RabbitMq/DB connection strings are validated the same way the template-events consumer's
        // already are (WaAdmin.WebApi/Program.cs's boot-time guard).
        services.AddScoped<ITenantResolver, WabaPhoneNumberTenantResolver>();
        services.AddHostedService<StopKeywordConsumerService>();
        services.AddHostedService<ErasureRequestProcessorService>();

        return services;
    }
}
