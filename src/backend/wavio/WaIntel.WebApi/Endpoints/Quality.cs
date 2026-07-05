using WaIntel.Application.Quality.Commands.SimulateQualityEvent;
using WaIntel.Application.Quality.Dtos;
using WaIntel.Application.Quality.Queries.GetHealthReport;
using WaIntel.Application.Quality.Queries.GetTierAdvisor;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.Utilities.Endpoints;
using wavio.Utilities.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace WaIntel.WebApi.Endpoints;

/// <summary>
/// GET  /api/v1/quality/health          weekly health report (spec §4.6) — tenant-scoped.
/// GET  /api/v1/quality/tier-advisor/{phoneNumberId}   tier-growth advisor for one number.
/// POST /api/v1/quality/simulate        QA/admin-only: fabricate a quality/tier event (issue #20
///                                       acceptance criterion). Same double-gate as Windows'
///                                       <c>/simulate</c> (issue #15): the route is only mapped
///                                       outside Production, the handler independently refuses
///                                       too, AND (stricter than Windows' precedent) it requires
///                                       the <c>quality.simulate</c> permission, which is granted
///                                       only to platform_admin.
///
/// Uses <see cref="ICurrentUser"/> + <see cref="ICurrentUser.RequireTenantId"/> (WaBilling's
/// Quotas.cs convention), NOT <c>ICurrentTenant.TenantId</c> (Windows.cs's convention) — found
/// live during issue #20 acceptance verification: <c>ICurrentTenant.TenantId</c> reads ONLY the
/// JWT's <c>tenant_id</c> claim, which a platform_admin token never carries (platform admins
/// aren't scoped to one tenant), so a platform_admin-only endpoint like <c>/simulate</c> could
/// never be called by the only role permitted to call it. <c>ICurrentUser.RequireTenantId()</c>
/// additionally honors the <c>X-Tenant-Id</c> header override
/// (<c>TenantResolutionMiddleware</c>) that platform admins are expected to pass, which is
/// exactly the WaBilling precedent this should have followed from the start.
/// </summary>
public sealed class Quality : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/quality";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.WithTags("Quality");

        groupBuilder.MapGet(GetHealth, "/health").RequireAuthorization("permission:quality.health.read");
        groupBuilder.MapGet(GetTierAdvisor, "/tier-advisor/{phoneNumberId:guid}")
            .RequireAuthorization("permission:quality.tier_advisor.read");

        // Impossible to reach in Production: the route itself is never registered there (same
        // pattern as Windows.cs's /simulate).
        var environment = ((IEndpointRouteBuilder)groupBuilder).ServiceProvider
            .GetRequiredService<IHostEnvironment>();
        if (!environment.IsProduction())
        {
            groupBuilder.MapPost(Simulate, "/simulate").RequireAuthorization("permission:quality.simulate");
        }
    }

    private static async Task<IResult> GetHealth(
        [FromQuery] Guid? phoneNumberId,
        ICurrentUser user,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new GetHealthReportQuery(user.RequireTenantId(), phoneNumberId), cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetTierAdvisor(
        Guid phoneNumberId,
        ICurrentUser user,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new GetTierAdvisorQuery(user.RequireTenantId(), phoneNumberId), cancellationToken);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> Simulate(
        SimulateQualityRequest request,
        ICurrentUser user,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(
            new SimulateQualityEventCommand(user.RequireTenantId(), request.PhoneNumberId, request.Rating, request.Tier),
            cancellationToken);

        return Results.Ok(result);
    }
}

public sealed record SimulateQualityRequest(Guid PhoneNumberId, string? Rating, string? Tier);
