using WaAdmin.Application.Onboarding.Commands.CompleteEmbeddedSignup;
using WaAdmin.Application.Onboarding.Commands.RefreshOnboardingStatus;
using WaAdmin.Application.Onboarding.Commands.RegisterPhoneNumber;
using WaAdmin.Application.Onboarding.Commands.RequestVerificationCode;
using WaAdmin.Application.Onboarding.Commands.UpdateBusinessProfile;
using WaAdmin.Application.Onboarding.Commands.VerifyPhoneCode;
using WaAdmin.Application.Onboarding.Dtos;
using WaAdmin.Application.Onboarding.Queries.GetBusinessProfile;
using WaAdmin.Application.Onboarding.Queries.GetOnboardingStatus;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.Utilities.ApiResponse.ResponseUtil;
using wavio.Utilities.Endpoints;
using wavio.Utilities.Services;

namespace WaAdmin.WebApi.Endpoints;

/// <summary>
/// WhatsApp onboarding wizard (/v1/onboarding, docs/ONBOARDING_WIZARD_PLAN.md, spec §4.1/§7.1):
/// Embedded Signup completion, phone registration + OTP, business profile, and the resumable
/// status checklist. Reads are <c>waba.onboarding.read</c> (Low risk); mutations are
/// <c>waba.onboarding.manage</c> (High risk → §8 step-up OTP applies automatically). The
/// Facebook login itself happens inside Meta's popup — no Meta credential ever transits these
/// endpoints; only the resulting authorization code does.
/// </summary>
public class Onboarding : IEndpointGroup
{
    public static string? RoutePrefix => "/v1/onboarding";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.WithTags("Onboarding").RequireAuthorization();

        groupBuilder.MapGet(GetStatus, "status").RequireAuthorization("permission:waba.onboarding.read");
        // refresh writes, but only mirrors Meta-owned review state — read permission by design
        // (the wizard polls it while waiting on Meta; a step-up prompt per poll would be absurd).
        groupBuilder.MapPost(Refresh, "refresh").RequireAuthorization("permission:waba.onboarding.read");
        groupBuilder.MapGet(GetProfile, "phone-numbers/{id:guid}/profile").RequireAuthorization("permission:waba.onboarding.read");

        groupBuilder.MapPost(EmbeddedSignup, "embedded-signup").RequireAuthorization("permission:waba.onboarding.manage");
        groupBuilder.MapPost(RegisterPhone, "phone-numbers/{id:guid}/register").RequireAuthorization("permission:waba.onboarding.manage");
        groupBuilder.MapPost(RequestCode, "phone-numbers/{id:guid}/request-code").RequireAuthorization("permission:waba.onboarding.manage");
        groupBuilder.MapPost(VerifyCode, "phone-numbers/{id:guid}/verify-code").RequireAuthorization("permission:waba.onboarding.manage");
        groupBuilder.MapPut(UpdateProfile, "phone-numbers/{id:guid}/profile").RequireAuthorization("permission:waba.onboarding.manage");
    }

    public static async Task<IResult> GetStatus(IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.QueryAsync(new GetOnboardingStatusQuery(), ct);
        return Results.Ok(new SingleResponse<OnboardingStatusDto> { Status = true, Data = data });
    }

    public static async Task<IResult> Refresh(ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(
            new RefreshOnboardingStatusCommand(user.RequireTenantId(), user.UserId), ct);
        return Results.Ok(new SingleResponse<OnboardingStatusDto> { Status = true, Data = data });
    }

    public static async Task<IResult> EmbeddedSignup(
        EmbeddedSignupRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(
            new CompleteEmbeddedSignupCommand(req, user.RequireTenantId(), user.UserId), ct);
        return Results.Ok(new SingleResponse<OnboardingStatusDto> { Status = true, Data = data });
    }

    public static async Task<IResult> RegisterPhone(
        Guid id, RegisterPhoneRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(
            new RegisterPhoneNumberCommand(id, req.Pin, user.RequireTenantId(), user.UserId), ct);
        return data is null ? Results.NotFound() : Results.Ok(new SingleResponse<OnboardingPhoneDto> { Status = true, Data = data });
    }

    public static async Task<IResult> RequestCode(
        Guid id, RequestCodeRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(
            new RequestVerificationCodeCommand(id, req.CodeMethod, req.Language, user.RequireTenantId()), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }

    public static async Task<IResult> VerifyCode(
        Guid id, VerifyCodeRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(
            new VerifyPhoneCodeCommand(id, req.Code, user.RequireTenantId(), user.UserId), ct);
        return data is null ? Results.NotFound() : Results.Ok(new SingleResponse<OnboardingPhoneDto> { Status = true, Data = data });
    }

    public static async Task<IResult> GetProfile(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.QueryAsync(new GetBusinessProfileQuery(id), ct);
        return data is null ? Results.NotFound() : Results.Ok(new SingleResponse<BusinessProfileDto> { Status = true, Data = data });
    }

    public static async Task<IResult> UpdateProfile(
        Guid id, UpdateBusinessProfileRequest req, ICurrentUser user, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(
            new UpdateBusinessProfileCommand(id, req, user.RequireTenantId(), user.UserId), ct);
        return data is null ? Results.NotFound() : Results.Ok(new SingleResponse<BusinessProfileDto> { Status = true, Data = data });
    }
}
