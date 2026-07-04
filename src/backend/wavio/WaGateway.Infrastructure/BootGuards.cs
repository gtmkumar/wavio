using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace WaGateway.Infrastructure;

/// <summary>
/// Eager, boot-time (Program.cs-callable) versions of checks that also exist as constructor
/// guards deeper in the DI graph (security review, PR #45, S2 — the same lesson already applied
/// to WaIngest issue #13 S2 and WaIntel issue #15 S1: fail closed at the top of Program.cs,
/// before the host ever accepts traffic, not only inside a component that's constructed lazily
/// on first use). Extracted into a plain static class (rather than left as top-level Program.cs
/// statements) so the condition logic is independently unit-testable.
/// </summary>
public static class BootGuards
{
    /// <summary>Mirrors <c>RabbitMqConnectionManager</c>'s own constructor guard — this is the
    /// fast, eager half of that same check.</summary>
    public static void RequireRabbitMqConfiguredOutsideDevelopment(IConfiguration configuration, IHostEnvironment environment)
    {
        var connStr = configuration.GetConnectionString("RabbitMq");
        if (string.IsNullOrWhiteSpace(connStr) && !environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "ConnectionStrings:RabbitMq is required outside Development. Provide it via " +
                "ConnectionStrings__RabbitMq env var or a secrets provider. Wavio will NOT start without it.");
        }
    }

    /// <summary>
    /// Without <c>Meta:Graph:BaseUrl</c>/<c>AccessToken</c> configured, every Graph send attempt
    /// throws <c>InvalidOperationException</c> (an invalid/relative request URI) — an exception
    /// type <c>OutboxDispatcherService</c>'s per-entry handling doesn't classify as transient or
    /// permanent, so it propagates to the tick-level catch-all, which logs and moves on WITHOUT
    /// reaching the retry/dead-letter logic for that entry. The entry stays claimed until the
    /// next stale-lock reclaim, which repeats the same failure forever: every message loops
    /// dispatch→reclaim indefinitely and never dead-letters. Caught here instead, at boot.
    /// </summary>
    public static void RequireMetaGraphConfiguredOutsideDevelopment(IConfiguration configuration, IHostEnvironment environment)
    {
        var baseUrl = configuration["Meta:Graph:BaseUrl"];
        var accessToken = configuration["Meta:Graph:AccessToken"];
        if ((string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(accessToken)) && !environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Meta:Graph:BaseUrl and Meta:Graph:AccessToken are required outside Development. Provide them via " +
                "Meta__Graph__BaseUrl / Meta__Graph__AccessToken env vars or a secrets provider. Wavio will NOT " +
                "start without them.");
        }
    }
}
