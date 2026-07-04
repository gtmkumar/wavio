using WaGateway.Application.Messages.Dtos;
using Wavio.Utilities.CQRS.Abstractions;

namespace WaGateway.Application.Messages.Commands.SendMessage;

/// <summary><c>POST /v1/messages</c> (spec §4.2). <paramref name="PayloadJson"/> is the raw,
/// type-specific body — see <see cref="WaGateway.Application.Messages.Logic.MessagePayloadValidator"/>.
/// <paramref name="IdempotencyKey"/> is the required header value, unique per tenant for 24h
/// (db/migrations/V007__messaging.sql).</summary>
public sealed record SendMessageCommand(
    Guid TenantId,
    Guid PhoneNumberId,
    string ToWaId,
    string MessageType,
    string PayloadJson,
    string IdempotencyKey) : ICommand<SendMessageResultDto>;
