using WaBilling.Application.Estimator.Dtos;
using WaBilling.Application.Estimator.Queries.EstimateCost;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.Utilities.ApiResponse.ResponseUtil;
using wavio.Utilities.Endpoints;
using wavio.Utilities.Services;

namespace WaBilling.WebApi.Endpoints;

/// <summary>/v1/costs (spec §4.7, issue #19): pre-send billable estimate for the tenant's own
/// campaign UIs — tenant-scoped (JWT/RLS).</summary>
public sealed class Costs : IEndpointGroup
{
    public static string? RoutePrefix => "/v1/costs";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.WithTags("Costs").RequireAuthorization();

        groupBuilder.MapGet(Estimate, "estimate").RequireAuthorization("permission:billing.costs.read");
    }

    private static async Task<IResult> Estimate(
        string category, string country, bool windowOpen, Guid? phoneNumberId,
        ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.QueryAsync(
            new EstimateCostQuery(user.RequireTenantId(), category, country, windowOpen, phoneNumberId), ct);
        return Results.Ok(new SingleResponse<CostEstimateDto> { Status = true, Data = data });
    }
}
