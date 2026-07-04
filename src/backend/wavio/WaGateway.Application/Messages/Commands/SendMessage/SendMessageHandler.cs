using System.Text.Json;
using WaGateway.Application.Common.Interfaces;
using WaGateway.Application.Messages.Dtos;
using WaGateway.Application.Messages.Logic;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.SharedDataModel.Entities.Messaging;
using wavio.Utilities.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace WaGateway.Application.Messages.Commands.SendMessage;

/// <summary>
/// Validates the typed payload, applies the window-aware send policy (ADR-005), and — for an
/// allowed send — writes <see cref="OutboundMessage"/> + <see cref="OutboundOutboxEntry"/> in one
/// transaction (the outbox pattern, spec §4.2). The actual Graph dispatch happens later, out of
/// request scope, in <c>OutboxDispatcherService</c> (WaGateway.Infrastructure).
///
/// Phone number ownership (security review, PR #45, S3): checked synchronously, before anything
/// else, so a foreign or nonexistent <c>PhoneNumberId</c> gets an immediate 404 instead of a
/// silent 202 that only surfaces as an UNRESOLVED_PHONE_NUMBER dead-letter minutes later, deep
/// in the dispatcher, with no synchronous feedback to the caller.
///
/// Idempotency (db/migrations/V007__messaging.sql, database-architect's handoff): checked FIRST
/// by a plain lookup (the common case — no DB conflict needed), then defended again at insert
/// time by catching the partial-unique-index violation for the rare concurrent-duplicate race.
/// Either path returns the ORIGINAL row's result, whatever its outcome (accepted or rejected) —
/// a retried request must be indistinguishable from the first one.
/// </summary>
public sealed class SendMessageHandler : ICommandHandler<SendMessageCommand, SendMessageResultDto>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IWaGatewayDbContext _db;
    private readonly IWindowStateClient _windowStateClient;

    public SendMessageHandler(IWaGatewayDbContext db, IWindowStateClient windowStateClient)
    {
        _db = db;
        _windowStateClient = windowStateClient;
    }

    public async Task<SendMessageResultDto> HandleAsync(SendMessageCommand command, CancellationToken cancellationToken)
    {
        var payloadErrors = MessagePayloadValidator.Validate(command.MessageType, command.PayloadJson);
        if (payloadErrors.Count > 0)
        {
            throw new ValidationException(
                new Dictionary<string, string[]> { ["payload"] = [.. payloadErrors] });
        }

        // Phone number ownership (security review, PR #45, S3): a foreign/nonexistent
        // PhoneNumberId used to be accepted here (202) and only ever surfaced as an async
        // UNRESOLVED_PHONE_NUMBER dead-letter in the dispatcher, minutes later, with no
        // synchronous feedback to the caller. One cheap, indexed AnyAsync turns that into an
        // immediate 404. RLS already scopes this query to the caller's own tenant on its own;
        // the explicit TenantId filter is defense in depth, matching every other query here.
        var phoneNumberBelongsToTenant = await _db.WabaPhoneNumbers
            .AnyAsync(p => p.TenantId == command.TenantId && p.Id == command.PhoneNumberId, cancellationToken);
        if (!phoneNumberBelongsToTenant)
        {
            throw new KeyNotFoundException($"Phone number {command.PhoneNumberId} was not found for this tenant.");
        }

        var existing = await FindByIdempotencyKeyAsync(command.TenantId, command.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return ToDto(existing);
        }

        string? templateCategory = command.MessageType == MessageTypes.Template
            ? JsonSerializer.Deserialize<TemplatePayload>(command.PayloadJson, JsonOptions)?.Category
            : null;

        var windowState = await _windowStateClient.GetWindowStateAsync(
            command.PhoneNumberId, command.ToWaId, cancellationToken);

        var policy = WindowPolicyEvaluator.Evaluate(
            command.MessageType, templateCategory,
            windowState?.CsOpen ?? false, windowState?.CtwaOpen ?? false);

        var now = DateTimeOffset.UtcNow;
        var message = new OutboundMessage
        {
            Id = Guid.NewGuid(),
            TenantId = command.TenantId,
            PhoneNumberId = command.PhoneNumberId,
            ToWaId = command.ToWaId,
            MessageType = command.MessageType,
            Payload = command.PayloadJson,
            IdempotencyKey = command.IdempotencyKey,
            IdempotencyActive = true,
            BillableEstimate = policy.BillableEstimate,
            AcceptedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };

        if (policy.Decision == SendDecision.RejectWindowClosed)
        {
            message.Status = "rejected";
            message.ErrorCode = "WINDOW_CLOSED";
            message.ErrorMessage =
                "No open customer-service or CTWA window for this recipient. Send a template instead " +
                "(ADR-005 — no silent free-form-to-template conversion).";
            _db.OutboundMessages.Add(message);
        }
        else
        {
            message.Status = "accepted";
            _db.OutboundMessages.Add(message);
            _db.OutboundOutboxEntries.Add(new OutboundOutboxEntry
            {
                Id = Guid.NewGuid(),
                TenantId = command.TenantId,
                OutboundMessageId = message.Id,
                PhoneNumberId = command.PhoneNumberId,
                Status = "pending",
                Attempts = 0,
                MaxAttempts = 5,
                NextAttemptAt = now,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsIdempotencyKeyConflict(ex))
        {
            // Lost the race to a concurrent duplicate request — the other request's row is now
            // the original; return its result instead of erroring.
            var original = await FindByIdempotencyKeyAsync(command.TenantId, command.IdempotencyKey, cancellationToken)
                ?? throw new InvalidOperationException(
                    "Unique-key conflict on outbound_messages but no matching row was found — " +
                    "this should be unreachable.");
            return ToDto(original);
        }

        return ToDto(message);
    }

    private Task<OutboundMessage?> FindByIdempotencyKeyAsync(Guid tenantId, string idempotencyKey, CancellationToken ct) =>
        _db.OutboundMessages
            .Where(m => m.TenantId == tenantId && m.IdempotencyKey == idempotencyKey && m.IdempotencyActive)
            .FirstOrDefaultAsync(ct);

    private static SendMessageResultDto ToDto(OutboundMessage message) =>
        new(message.Id, message.Status, message.Wamid, message.BillableEstimate, message.ErrorCode, message.ErrorMessage);

    /// <summary>
    /// Detects a violation of <c>outbound_messages_tenant_id_idempotency_key_key</c> without a
    /// hard Npgsql dependency in the Application layer — same reflection-based SqlState read as
    /// <c>wavio.Utilities.Middlewares.ExceptionsMiddleware.ExceptionHandler</c>. Safe to treat ANY
    /// 23505 on this specific insert as the idempotency-key conflict: <c>wamid</c> is always null
    /// at insert time and its partial unique index only applies <c>WHERE wamid IS NOT NULL</c>,
    /// so no other unique constraint on this table can fire here.
    /// </summary>
    private static bool IsIdempotencyKeyConflict(DbUpdateException ex)
    {
        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            var prop = inner.GetType().GetProperty("SqlState");
            if (prop?.GetValue(inner) is string state && state == "23505")
            {
                return true;
            }
        }
        return false;
    }
}
