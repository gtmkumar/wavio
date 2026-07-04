using System.Text.Json;

namespace WaGateway.Application.Messages.Dtos;

/// <summary>HTTP request body for <c>POST /v1/messages</c>. The required <c>Idempotency-Key</c>
/// header is read separately by the endpoint (spec §4.2) — it is not part of the body.</summary>
public sealed record SendMessageRequest(Guid PhoneNumberId, string ToWaId, string MessageType, JsonElement Payload);
