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

        // Lint pipeline (issue #27): both registrations resolve as IEnumerable<ITemplateLintService>
        // in TemplateSubmissionService, which runs every one of them and records a
        // template_lint_results row per linter. Rules always runs. The LLM pass is registered only
        // when Lint:Llm:Enabled is true — when it's not, the pipeline is rules-only by omission,
        // not by a runtime branch (see LlmTemplateLintService's own doc comment).
        services.AddScoped<ITemplateLintService, RulesTemplateLintService>();

        // ValidateOnStart: Enabled=true with a missing ApiKey or a non-https/invalid BaseUrl
        // fails the host at boot with a clear message, instead of every lint run silently
        // degrading to "skipped" (missing key) or 500ing inside the typed-client factory
        // (bad BaseUrl) — same fail-fast posture as the Meta:Graph boot guard (security
        // review, issue #27 finding 1).
        services.AddOptionsWithValidateOnStart<LintLlmOptions>()
            .Bind(configuration.GetSection(LintLlmOptions.SectionName))
            .Validate(LintLlmOptions.IsValid,
                "Lint:Llm is enabled but misconfigured: ApiKey must be set and BaseUrl must be an absolute https:// URL.");

        var llmEnabled = configuration.GetSection(LintLlmOptions.SectionName).GetValue<bool>("Enabled");
        if (llmEnabled)
        {
            services.AddHttpClient<ITemplateLintService, LlmTemplateLintService>((sp, client) =>
                {
                    var llmOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LintLlmOptions>>().Value;
                    client.BaseAddress = new Uri(llmOptions.BaseUrl);
                })
                // HttpClientFactory's built-in handlers log request headers at Trace level;
                // without this, flipping System.Net.Http.HttpClient to Trace while debugging
                // would write the Anthropic key to logs (issue #27 finding 2).
                .RedactLoggedHeaders(["x-api-key"]);
        }

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
