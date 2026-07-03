using operations.Application.Example;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.Utilities.ApiResponse.ResponseUtil;
using wavio.Utilities.Endpoints;

namespace operations.WebApi.Endpoints.Example;

/// <summary>
/// Example CRUD vertical slice (/api/v1/widgets) — copy this shape for a new feature:
/// Entity → EF Configuration → DbContext DbSet → *DbContext-interface surface →
/// CQRS command/query handlers → this IEndpointGroup. Delete once the project has its
/// own real domain entities.
/// </summary>
public class Widgets : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/widgets";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Example - Widgets").RequireAuthorization();

        group.MapGet(GetWidgets);
        group.MapGet(GetWidgetById, "{id:guid}");
        group.MapPost(CreateWidget);
        group.MapPut(UpdateWidget, "{id:guid}");
        group.MapDelete(DeleteWidget, "{id:guid}");
    }

    public static async Task<IResult> GetWidgets(IDispatcher dispatcher, CancellationToken ct, int page = 1, int pageSize = 20)
    {
        var data = await dispatcher.QueryAsync(new GetWidgetsQuery(page, pageSize), ct);
        return Results.Ok(new PaginatedListResponse<WidgetDto> { Status = true, Data = data });
    }

    public static async Task<IResult> GetWidgetById(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.QueryAsync(new GetWidgetByIdQuery(id), ct);
        return data is null ? Results.NotFound() : Results.Ok(new SingleResponse<WidgetDto> { Status = true, Data = data });
    }

    public static async Task<IResult> CreateWidget(CreateWidgetRequest req, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new CreateWidgetCommand(req), ct);
        return Results.Ok(new SingleResponse<WidgetDto> { Status = true, Data = data });
    }

    public static async Task<IResult> UpdateWidget(Guid id, UpdateWidgetRequest req, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new UpdateWidgetCommand(id, req), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }

    public static async Task<IResult> DeleteWidget(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new DeleteWidgetCommand(id), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }
}
