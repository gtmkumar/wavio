using WaGateway.Application.Messages.Commands.SendMessage;
using WaGateway.Application.Messages.Dtos;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.SharedDataModel.Contracts;
using wavio.Utilities.Endpoints;
using wavio.Utilities.Exceptions;
using wavio.Utilities.Validation;
using Microsoft.AspNetCore.Mvc;

namespace WaGateway.WebApi.Endpoints;

/// <summary>
/// POST /api/v1/messages — the single internal send API for all typed message payloads (spec
/// §4.2). Requires the <c>Idempotency-Key</c> header (24h dedupe window,
/// db/migrations/V007__messaging.sql) and the <c>messages.send</c> permission.
/// </summary>
public sealed class Messages : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/messages";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.WithTags("Messages");

        groupBuilder.MapPost(Send, "")
            .AddEndpointFilter<ValidationFilter<SendMessageRequest>>()
            .RequireAuthorization("permission:messages.send");
    }

    private static async Task<IResult> Send(
        SendMessageRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        ICurrentTenant currentTenant,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ValidationException(
                new Dictionary<string, string[]> { ["Idempotency-Key"] = ["The Idempotency-Key header is required."] });
        }

        if (currentTenant.TenantId is not { } tenantId)
        {
            return Results.Unauthorized();
        }

        var result = await dispatcher.SendAsync(
            new SendMessageCommand(
                tenantId,
                request.PhoneNumberId,
                request.ToWaId,
                request.MessageType,
                request.Payload.GetRawText(),
                idempotencyKey),
            cancellationToken);

        if (result.Status == "rejected")
        {
            throw new StructuredBusinessRuleException(
                result.ErrorCode ?? "REJECTED",
                result.ErrorMessage ?? "Send rejected.",
                new Dictionary<string, string> { ["outboundMessageId"] = result.Id.ToString() });
        }

        return Results.Ok(result);
    }
}
