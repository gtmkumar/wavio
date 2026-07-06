using WaAdmin.Application.Waba.Dtos;
using WaAdmin.Application.Waba.Queries.GetPhoneNumbers;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.Utilities.ApiResponse.ResponseUtil;
using wavio.Utilities.Endpoints;

namespace WaAdmin.WebApi.Endpoints;

/// <summary>
/// Read-only WABA lookups for the admin console (/v1/waba). Deliberately not the WABA
/// onboarding/management surface (issue #6/#14) — just enough for pickers: which sender
/// numbers (and thereby business accounts) exist in the caller's tenant.
/// </summary>
public class Waba : IEndpointGroup
{
    public static string? RoutePrefix => "/v1/waba";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.WithTags("Waba").RequireAuthorization();

        groupBuilder.MapGet(GetPhoneNumbers, "phone-numbers")
            .RequireAuthorization("permission:waba.phone_numbers.read");
    }

    public static async Task<IResult> GetPhoneNumbers(IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.QueryAsync(new GetPhoneNumbersQuery(), ct);
        return Results.Ok(new ListResponse<PhoneNumberSummaryDto> { Status = true, Data = data });
    }
}
