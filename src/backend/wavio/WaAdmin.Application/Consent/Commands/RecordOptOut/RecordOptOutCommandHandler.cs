using WaAdmin.Application.Common.Interfaces;
using WaAdmin.Application.Consent.Dtos;
using wavio.SharedDataModel.Entities.Consent;
using wavio.SharedDataModel.Entities.Messaging;
using wavio.Utilities.Exceptions;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace WaAdmin.Application.Consent.Commands.RecordOptOut;

/// <summary>
/// Writes the opt-out ledger row and upserts the (tenant, wa_id) marketing suppression in one
/// unit of work (spec §4.10). Idempotent on STOP-listener redelivery: a stop_keyword opt-out
/// carries the triggering inbound wamid, and a prior row with the SAME (tenant, inbound_wamid)
/// short-circuits to a no-op return of that existing row — there is no DB unique constraint on
/// inbound_wamid (V012 doesn't have one), so this app-level check is the only idempotency guard;
/// see StopKeywordConsumerService's doc comment for why that's acceptable (single-instance
/// consumer, manual ack after this handler commits).
/// </summary>
public sealed class RecordOptOutCommandHandler : ICommandHandler<RecordOptOutCommand, OptOutEventDto>
{
    private static readonly HashSet<string> ValidScopes = ["marketing", "all"];
    private static readonly HashSet<string> ValidReasons = ["stop_keyword", "manual", "complaint", "bounce"];

    private readonly IWaAdminDbContext _db;

    public RecordOptOutCommandHandler(IWaAdminDbContext db) => _db = db;

    public async Task<OptOutEventDto> HandleAsync(RecordOptOutCommand command, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(command.WaId))
            errors["waId"] = ["waId is required."];
        if (!ValidScopes.Contains(command.Scope))
            errors["scope"] = [$"scope must be one of: {string.Join(", ", ValidScopes)}."];
        if (!ValidReasons.Contains(command.Reason))
            errors["reason"] = [$"reason must be one of: {string.Join(", ", ValidReasons)}."];
        if (errors.Count > 0)
            throw new ValidationException(errors);

        if (command.Reason == "stop_keyword" && !string.IsNullOrWhiteSpace(command.InboundWamid))
        {
            var existing = await _db.OptOutEvents.AsNoTracking().FirstOrDefaultAsync(
                o => o.TenantId == command.TenantId && o.InboundWamid == command.InboundWamid,
                cancellationToken);
            if (existing is not null)
            {
                return ToDto(existing);
            }
        }

        var now = DateTimeOffset.UtcNow;
        var optOut = new OptOutEvent
        {
            Id = Guid.NewGuid(),
            TenantId = command.TenantId,
            WaId = command.WaId,
            Scope = command.Scope,
            Reason = command.Reason,
            Keyword = command.Keyword,
            Language = command.Language,
            InboundWamid = command.InboundWamid,
            Payload = command.PayloadJson,
            OccurredAt = now,
            CreatedAt = now,
            CreatedBy = command.ActorId,
        };
        _db.OptOutEvents.Add(optOut);

        // Enforcement side, same unit of work (spec §4.10). A row here means "no marketing"
        // regardless of Scope — see SuppressionListEntry's doc comment for why scope='all' still
        // only produces one suppression_list row.
        var suppression = await _db.SuppressionListEntries.FirstOrDefaultAsync(
            s => s.TenantId == command.TenantId && s.WaId == command.WaId, cancellationToken);
        if (suppression is null)
        {
            _db.SuppressionListEntries.Add(new SuppressionListEntry
            {
                Id = Guid.NewGuid(),
                TenantId = command.TenantId,
                WaId = command.WaId,
                Reason = command.Reason,
                Source = command.Reason == "stop_keyword" ? "stop_listener" : "consent_api",
                CreatedAt = now,
                UpdatedAt = now,
                CreatedBy = command.ActorId,
                UpdatedBy = command.ActorId,
            });
        }
        else
        {
            // Re-suppressing an already-suppressed number (e.g. a second STOP) refreshes the
            // reason/timestamp and clears any prior expiry — a fresh opt-out is never weaker than
            // an existing one.
            suppression.Reason = command.Reason;
            suppression.Source = command.Reason == "stop_keyword" ? "stop_listener" : "consent_api";
            suppression.ExpiresAt = null;
            suppression.UpdatedAt = now;
            suppression.UpdatedBy = command.ActorId;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(optOut);
    }

    private static OptOutEventDto ToDto(OptOutEvent optOut) => new(
        optOut.Id, optOut.WaId, optOut.Scope, optOut.Reason, optOut.Keyword, optOut.Language, optOut.OccurredAt);
}
