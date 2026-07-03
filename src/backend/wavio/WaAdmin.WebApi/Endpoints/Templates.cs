using WaAdmin.Application.Templates.Commands.CreateTemplate;
using WaAdmin.Application.Templates.Commands.DeleteTemplate;
using WaAdmin.Application.Templates.Commands.SubmitTemplate;
using WaAdmin.Application.Templates.Commands.UpdateTemplate;
using WaAdmin.Application.Templates.Dtos;
using WaAdmin.Application.Templates.Queries.GetTemplateById;
using WaAdmin.Application.Templates.Queries.GetTemplates;
using WaAdmin.Application.Templates.Queries.GetTemplateStatus;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.Utilities.ApiResponse.ResponseUtil;
using wavio.Utilities.Endpoints;
using wavio.Utilities.Services;

namespace WaAdmin.WebApi.Endpoints;

/// <summary>
/// Template lifecycle (/v1/templates, spec §7.1, issue #16): CRUD + submit-to-Meta + status
/// history. Every mutation dispatches a command through <see cref="IDispatcher"/>; the state
/// machine and immutability rules live in the Application-layer handlers, not here.
/// </summary>
public class Templates : IEndpointGroup
{
    public static string? RoutePrefix => "/v1/templates";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.WithTags("Templates").RequireAuthorization();

        groupBuilder.MapGet(GetTemplates).RequireAuthorization("permission:templates.list");
        groupBuilder.MapGet(GetTemplateById, "{id:guid}").RequireAuthorization("permission:templates.read");
        groupBuilder.MapGet(GetTemplateStatus, "{id:guid}/status").RequireAuthorization("permission:templates.read");
        groupBuilder.MapPost(CreateTemplate).RequireAuthorization("permission:templates.create");
        groupBuilder.MapPut(UpdateTemplate, "{id:guid}").RequireAuthorization("permission:templates.update");
        groupBuilder.MapPost(SubmitTemplate, "{id:guid}/submit").RequireAuthorization("permission:templates.submit");
        groupBuilder.MapDelete(DeleteTemplate, "{id:guid}").RequireAuthorization("permission:templates.delete");
    }

    public static async Task<IResult> GetTemplates(
        string? status, string? category, Guid? businessAccountId,
        IDispatcher dispatcher, CancellationToken ct, int page = 1, int pageSize = 20)
    {
        // Raw values passed straight through — GetTemplatesQueryHandler is the single place that
        // clamps page/pageSize (including the upper bound; security review S2), so there is only
        // one bound to keep in sync.
        var data = await dispatcher.QueryAsync(
            new GetTemplatesQuery(page, pageSize, status, category, businessAccountId), ct);
        return Results.Ok(new PaginatedListResponse<TemplateDto> { Status = true, Data = data });
    }

    public static async Task<IResult> GetTemplateById(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.QueryAsync(new GetTemplateByIdQuery(id), ct);
        return data is null ? Results.NotFound() : Results.Ok(new SingleResponse<TemplateDto> { Status = true, Data = data });
    }

    public static async Task<IResult> GetTemplateStatus(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.QueryAsync(new GetTemplateStatusQuery(id), ct);
        return data is null ? Results.NotFound() : Results.Ok(new SingleResponse<TemplateStatusDto> { Status = true, Data = data });
    }

    public static async Task<IResult> CreateTemplate(
        CreateTemplateRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(
            new CreateTemplateCommand(req, user.RequireTenantId(), user.UserId), ct);
        return Results.Created($"/v1/templates/{data.Template.Id}", new SingleResponse<CreateTemplateResult> { Status = true, Data = data });
    }

    public static async Task<IResult> UpdateTemplate(
        Guid id, UpdateTemplateRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(
            new UpdateTemplateCommand(id, req, user.RequireTenantId(), user.UserId), ct);
        return data is null ? Results.NotFound() : Results.Ok(new SingleResponse<TemplateDto> { Status = true, Data = data });
    }

    public static async Task<IResult> SubmitTemplate(Guid id, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(
            new SubmitTemplateCommand(id, user.RequireTenantId(), user.UserId), ct);
        return Results.Ok(new SingleResponse<CreateTemplateResult> { Status = true, Data = data });
    }

    public static async Task<IResult> DeleteTemplate(Guid id, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new DeleteTemplateCommand(id, user.RequireTenantId(), user.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }
}
