using WaBilling.Application.Quotas.Commands.CheckQuota;
using WaBilling.Application.Quotas.Dtos;
using WaBilling.Application.Quotas.Queries.GetQuotaStatus;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.Utilities.ApiResponse.ResponseUtil;
using wavio.Utilities.Endpoints;
using wavio.Utilities.Services;

namespace WaBilling.WebApi.Endpoints;

/// <summary>/v1/quotas (spec §4.7, issue #19): per-tenant metering and the send-time gate.
/// Tenant-scoped (JWT/RLS) — a caller only ever checks/sees its own tenant's quotas.</summary>
public sealed class Quotas : IEndpointGroup
{
    public static string? RoutePrefix => "/v1/quotas";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.WithTags("Quotas").RequireAuthorization();

        groupBuilder.MapGet(GetStatus, "status").RequireAuthorization("permission:billing.quotas.read");
        // Called by wa-gateway-svc immediately before dispatching a send (same "HTTP hop between
        // services, forwarding the caller's JWT/tenant context" pattern as the gateway's
        // window-state lookup against wa-intel-svc, issue #14).
        groupBuilder.MapPost(Check, "check").RequireAuthorization("permission:billing.quotas.check");
    }

    private static async Task<IResult> GetStatus(
        ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.QueryAsync(new GetQuotaStatusQuery(user.RequireTenantId()), ct);
        return Results.Ok(new ListResponse<QuotaStatusEntryDto> { Status = true, Data = data });
    }

    private static async Task<IResult> Check(
        CheckQuotaRequest request, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(
            new CheckQuotaCommand(user.RequireTenantId(), request.Category), ct);
        return Results.Ok(new SingleResponse<QuotaCheckResultDto> { Status = true, Data = data });
    }
}

public sealed record CheckQuotaRequest(string Category);
