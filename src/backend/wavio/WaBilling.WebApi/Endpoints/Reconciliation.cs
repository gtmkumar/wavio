using WaBilling.Application.Reconciliation.Dtos;
using WaBilling.Application.Reconciliation.Queries.GetReconciliation;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.Utilities.ApiResponse.ResponseUtil;
using wavio.Utilities.Endpoints;
using wavio.Utilities.Services;

namespace WaBilling.WebApi.Endpoints;

/// <summary>/v1/reconciliation (spec §4.7, issue #19): minimal-v1 ledger-vs-invoice-feed variance
/// report for a tenant's billing period. Tenant-scoped (JWT/RLS).</summary>
public sealed class Reconciliation : IEndpointGroup
{
    public static string? RoutePrefix => "/v1/reconciliation";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.WithTags("Reconciliation").RequireAuthorization();

        groupBuilder.MapGet(GetReport, "").RequireAuthorization("permission:billing.reconciliation.read");
    }

    private static async Task<IResult> GetReport(
        DateOnly periodStart, DateOnly periodEnd, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.QueryAsync(
            new GetReconciliationQuery(user.RequireTenantId(), periodStart, periodEnd), ct);
        return Results.Ok(new SingleResponse<ReconciliationDto> { Status = true, Data = data });
    }
}
