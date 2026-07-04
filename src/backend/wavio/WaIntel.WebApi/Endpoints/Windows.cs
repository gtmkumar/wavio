using WaIntel.Application.Windows.Commands.SimulateWindow;
using WaIntel.Application.Windows.Queries.GetWindowState;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.SharedDataModel.Contracts;
using wavio.Utilities.Endpoints;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace WaIntel.WebApi.Endpoints;

/// <summary>
/// GET  /api/v1/windows/{waId}   window state (spec §7.1) — tenant-scoped via JWT + RLS.
/// POST /api/v1/windows/simulate QA-only: fabricate an exact window state (issue #15). The route
///                                is only mapped at all outside Production — see <see cref="Map"/>
///                                — and the handler independently refuses in Production too
///                                (fail closed, two gates, not one).
/// </summary>
public sealed class Windows : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/windows";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.WithTags("Windows");

        groupBuilder.MapGet(GetWindow, "/{waId}").RequireAuthorization();

        // Impossible to reach in Production: the route itself is never registered there, not
        // just guarded after the fact.
        var environment = ((IEndpointRouteBuilder)groupBuilder).ServiceProvider
            .GetRequiredService<IHostEnvironment>();
        if (!environment.IsProduction())
        {
            groupBuilder.MapPost(Simulate, "/simulate").RequireAuthorization();
        }
    }

    private static async Task<IResult> GetWindow(
        string waId,
        [FromQuery] Guid? phoneNumberId,
        ICurrentTenant currentTenant,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } tenantId)
        {
            return Results.Unauthorized();
        }

        var result = await dispatcher.QueryAsync(
            new GetWindowStateQuery(tenantId, waId, phoneNumberId), cancellationToken);

        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> Simulate(
        SimulateWindowRequest request,
        ICurrentTenant currentTenant,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } tenantId)
        {
            return Results.Unauthorized();
        }

        var result = await dispatcher.SendAsync(
            new SimulateWindowCommand(
                tenantId,
                request.PhoneNumberId,
                request.WaId,
                request.Origin ?? "organic",
                request.CsExpiresAt,
                request.CtwaExpiresAt),
            cancellationToken);

        return Results.Ok(result);
    }
}

public sealed record SimulateWindowRequest(
    Guid PhoneNumberId,
    string WaId,
    string? Origin,
    DateTimeOffset? CsExpiresAt,
    DateTimeOffset? CtwaExpiresAt);
