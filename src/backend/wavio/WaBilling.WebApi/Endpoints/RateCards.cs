using WaBilling.Application.RateCards.Commands.UpsertRateCard;
using WaBilling.Application.RateCards.Dtos;
using WaBilling.Application.RateCards.Queries.GetRateCards;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.Utilities.ApiResponse.ResponseUtil;
using wavio.Utilities.Endpoints;
using wavio.Utilities.Services;
using wavio.Utilities.Validation;

namespace WaBilling.WebApi.Endpoints;

/// <summary>
/// /v1/rate-cards (spec §4.7, issue #19): admin CRUD for Meta's versioned rate card. Platform-
/// admin scoped — the rate card is identical for every tenant on a given currency, so this is not
/// tenant-scoped data (no RLS on billing.rate_cards/rate_card_entries).
/// </summary>
public sealed class RateCards : IEndpointGroup
{
    public static string? RoutePrefix => "/v1/rate-cards";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.WithTags("RateCards").RequireAuthorization();

        groupBuilder.MapGet(GetRateCards).RequireAuthorization("permission:billing.rate_cards.read");
        groupBuilder.MapPost(CreateRateCard, "")
            .AddEndpointFilter<ValidationFilter<UpsertRateCardRequest>>()
            .RequireAuthorization("permission:billing.rate_cards.manage");
        groupBuilder.MapPut(UpdateRateCard, "{id:guid}")
            .AddEndpointFilter<ValidationFilter<UpsertRateCardRequest>>()
            .RequireAuthorization("permission:billing.rate_cards.manage");
    }

    private static async Task<IResult> GetRateCards(
        string? currency, string? status, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.QueryAsync(new GetRateCardsQuery(currency, status), ct);
        return Results.Ok(new ListResponse<RateCardDto> { Status = true, Data = data });
    }

    private static async Task<IResult> CreateRateCard(
        UpsertRateCardRequest request, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new UpsertRateCardCommand(null, request, user.UserId), ct);
        return Results.Created($"/v1/rate-cards/{data.Id}", new SingleResponse<RateCardDto> { Status = true, Data = data });
    }

    private static async Task<IResult> UpdateRateCard(
        Guid id, UpsertRateCardRequest request, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(new UpsertRateCardCommand(id, request, user.UserId), ct);
        return Results.Ok(new SingleResponse<RateCardDto> { Status = true, Data = data });
    }
}
