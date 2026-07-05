using WaAdmin.Application.Consent.Commands.RecordOptIn;
using WaAdmin.Application.Consent.Commands.RecordOptOut;
using WaAdmin.Application.Consent.Dtos;
using WaAdmin.Application.Consent.Queries.GetConsentState;
using WaAdmin.Application.ErasureRequests.Commands.CreateErasureRequest;
using WaAdmin.Application.ErasureRequests.Queries.GetErasureRequestById;
using WaAdmin.Application.RetentionPolicies.Commands.UpsertRetentionPolicy;
using WaAdmin.Application.RetentionPolicies.Queries.GetRetentionPolicies;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.Utilities.ApiResponse.ResponseUtil;
using wavio.Utilities.Endpoints;
using wavio.Utilities.Services;

namespace WaAdmin.WebApi.Endpoints;

/// <summary>
/// Consent ledger (DPDP Act 2023) — opt-in evidence, opt-out/suppression, data-principal rights,
/// retention policies (/v1/consent, spec §4.10, issue #21). Every mutation dispatches a command
/// through <see cref="IDispatcher"/>; the STOP-keyword path (reason=stop_keyword) is written
/// exclusively by <c>StopKeywordConsumerService</c>, never through this HTTP surface — a caller
/// cannot forge keyword-detection provenance.
/// </summary>
public sealed class Consent : IEndpointGroup
{
    public static string? RoutePrefix => "/v1/consent";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.WithTags("Consent").RequireAuthorization();

        groupBuilder.MapPost(RecordOptIn, "opt-in").RequireAuthorization("permission:consent.write");
        groupBuilder.MapPost(RecordOptOut, "opt-out").RequireAuthorization("permission:consent.write");
        groupBuilder.MapGet(GetConsentState, "{waId}").RequireAuthorization("permission:consent.read");

        groupBuilder.MapPost(CreateErasureRequest, "requests").RequireAuthorization("permission:consent.requests.manage");
        groupBuilder.MapGet(GetErasureRequestById, "requests/{id:guid}").RequireAuthorization("permission:consent.requests.read");

        groupBuilder.MapGet(GetRetentionPolicies, "retention-policies").RequireAuthorization("permission:consent.retention.read");
        groupBuilder.MapPut(UpsertRetentionPolicy, "retention-policies").RequireAuthorization("permission:consent.retention.manage");
    }

    private static async Task<IResult> RecordOptIn(
        RecordOptInRequest req, HttpContext httpContext, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(
            new RecordOptInCommand(req, user.RequireTenantId(), user.UserId, httpContext.Connection.RemoteIpAddress), ct);
        return Results.Created($"/v1/consent/{data.WaId}", new SingleResponse<OptInEventDto> { Status = true, Data = data });
    }

    private static async Task<IResult> RecordOptOut(
        RecordManualOptOutRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        // Manual API path only accepts manual/complaint — stop_keyword is reserved for the
        // STOP-listener consumer (see class doc comment). RecordOptOutCommandHandler's own guard
        // clause already rejects an out-of-vocabulary reason, but that clause allows "bounce" too
        // (a future webhook-driven path); reject stop_keyword specifically HERE, at the HTTP
        // boundary, so this endpoint can never be used to forge listener provenance. The global
        // IExceptionHandler owns turning this into a response (CLAUDE.md — no try/catch wallpaper
        // in endpoints), same convention as every command handler's guard clauses.
        if (req.Reason == "stop_keyword")
        {
            throw new wavio.Utilities.Exceptions.ValidationException(new Dictionary<string, string[]>
            {
                ["reason"] = ["stop_keyword may only be recorded by the STOP-keyword listener, not this endpoint."],
            });
        }

        var payloadJson = req.Notes is null ? null : System.Text.Json.JsonSerializer.Serialize(new { notes = req.Notes });
        var data = await dispatcher.SendAsync(
            new RecordOptOutCommand(
                user.RequireTenantId(), req.WaId, req.Scope, req.Reason,
                Keyword: null, Language: null, InboundWamid: null, payloadJson, user.UserId),
            ct);
        return Results.Ok(new SingleResponse<OptOutEventDto> { Status = true, Data = data });
    }

    private static async Task<IResult> GetConsentState(
        string waId, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.QueryAsync(new GetConsentStateQuery(user.RequireTenantId(), waId), ct);
        return Results.Ok(new SingleResponse<ConsentStateDto> { Status = true, Data = data });
    }

    private static async Task<IResult> CreateErasureRequest(
        CreateErasureRequestRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(
            new CreateErasureRequestCommand(req, user.RequireTenantId(), user.UserId), ct);
        return Results.Created($"/v1/consent/requests/{data.Id}", new SingleResponse<ErasureRequestDto> { Status = true, Data = data });
    }

    private static async Task<IResult> GetErasureRequestById(
        Guid id, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.QueryAsync(new GetErasureRequestByIdQuery(user.RequireTenantId(), id), ct);
        return data is null ? Results.NotFound() : Results.Ok(new SingleResponse<ErasureRequestDto> { Status = true, Data = data });
    }

    private static async Task<IResult> GetRetentionPolicies(
        ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.QueryAsync(new GetRetentionPoliciesQuery(user.RequireTenantId()), ct);
        return Results.Ok(new ListResponse<RetentionPolicyDto> { Status = true, Data = data });
    }

    private static async Task<IResult> UpsertRetentionPolicy(
        UpsertRetentionPolicyRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(
            new UpsertRetentionPolicyCommand(req, user.RequireTenantId(), user.UserId), ct);
        return Results.Ok(new SingleResponse<RetentionPolicyDto> { Status = true, Data = data });
    }
}
