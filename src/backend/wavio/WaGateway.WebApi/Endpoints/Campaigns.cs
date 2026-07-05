using WaGateway.Application.Campaigns.Commands.CancelCampaign;
using WaGateway.Application.Campaigns.Commands.CreateCampaign;
using WaGateway.Application.Campaigns.Commands.LaunchCampaign;
using WaGateway.Application.Campaigns.Dtos;
using WaGateway.Application.Campaigns.Queries.GetCampaign;
using WaGateway.Application.Campaigns.Queries.ListCampaigns;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.SharedDataModel.Contracts;
using wavio.Utilities.Endpoints;
using wavio.Utilities.Validation;

namespace WaGateway.WebApi.Endpoints;

/// <summary>
/// POST /api/v1/campaigns — the broadcast-with-tier-aware-chunking engine (spec §4.2/§7.1, issue
/// #22). Creation/launch/cancel are synchronous accept-time operations; the actual per-recipient
/// dispatch happens later, out of request scope, in <c>CampaignChunkerService</c>
/// (WaGateway.Infrastructure) — same "accept now, dispatch later" split as the outbox pattern
/// (issue #14).
/// </summary>
public sealed class Campaigns : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/campaigns";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.WithTags("Campaigns");

        groupBuilder.MapPost(Create, "")
            .AddEndpointFilter<ValidationFilter<CreateCampaignRequest>>()
            .RequireAuthorization("permission:campaigns.create");

        groupBuilder.MapPost(Launch, "{id}/launch")
            .RequireAuthorization("permission:campaigns.launch");

        groupBuilder.MapPost(Cancel, "{id}/cancel")
            .RequireAuthorization("permission:campaigns.cancel");

        groupBuilder.MapGet(GetOne, "{id}")
            .RequireAuthorization("permission:campaigns.read");

        groupBuilder.MapGet(List, "")
            .RequireAuthorization("permission:campaigns.list");
    }

    private static async Task<IResult> Create(
        CreateCampaignRequest request, ICurrentTenant currentTenant, IDispatcher dispatcher, CancellationToken ct)
    {
        if (currentTenant.TenantId is not { } tenantId)
        {
            return Results.Unauthorized();
        }

        var result = await dispatcher.SendAsync(
            new CreateCampaignCommand(
                tenantId, request.Name, request.PhoneNumberId, request.TemplateVersionId,
                request.ParamsJson, request.Audience, request.ScheduledAt, request.Country),
            ct);

        return Results.Ok(result);
    }

    private static async Task<IResult> Launch(Guid id, ICurrentTenant currentTenant, IDispatcher dispatcher, CancellationToken ct)
    {
        if (currentTenant.TenantId is not { } tenantId)
        {
            return Results.Unauthorized();
        }

        var result = await dispatcher.SendAsync(new LaunchCampaignCommand(tenantId, id), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> Cancel(Guid id, ICurrentTenant currentTenant, IDispatcher dispatcher, CancellationToken ct)
    {
        if (currentTenant.TenantId is not { } tenantId)
        {
            return Results.Unauthorized();
        }

        var result = await dispatcher.SendAsync(new CancelCampaignCommand(tenantId, id), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetOne(Guid id, ICurrentTenant currentTenant, IDispatcher dispatcher, CancellationToken ct)
    {
        if (currentTenant.TenantId is not { } tenantId)
        {
            return Results.Unauthorized();
        }

        var result = await dispatcher.QueryAsync(new GetCampaignQuery(tenantId, id), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> List(
        string? status, ICurrentTenant currentTenant, IDispatcher dispatcher, CancellationToken ct)
    {
        if (currentTenant.TenantId is not { } tenantId)
        {
            return Results.Unauthorized();
        }

        var result = await dispatcher.QueryAsync(new ListCampaignsQuery(tenantId, status), ct);
        return Results.Ok(result);
    }
}
