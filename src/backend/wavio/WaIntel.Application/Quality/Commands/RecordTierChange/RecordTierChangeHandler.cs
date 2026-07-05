using WaIntel.Application.Common.Interfaces;
using WaIntel.Application.Quality.Dtos;
using WaIntel.Application.Quality.Logic;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.SharedDataModel.Entities.Quality;
using WaPlatform.Contracts.IntegrationEvents.V1;
using Microsoft.EntityFrameworkCore;

namespace WaIntel.Application.Quality.Commands.RecordTierChange;

/// <summary>
/// Applies a messaging-tier change: writes the append-only <c>messaging_tier_events</c> row (in
/// the platform's canonical tier vocabulary), updates <c>waba.phone_numbers.messaging_tier</c>
/// (Meta's raw code, matching issue #19's convention), and opens a <c>tier_downgrade</c> incident
/// when applicable.
///
/// Prefers the number's CURRENTLY STORED tier as the "old" value over the event's own
/// <c>PreviousTier</c> (which WaIngest's normalizer reports as null in Wave 1 — see
/// <c>TierChangedV1</c>'s doc comment: "Wave 2 #20 Guardian" owns that state), same reasoning as
/// <c>RecordQualityChangeHandler</c>.
/// </summary>
public sealed class RecordTierChangeHandler : ICommandHandler<RecordTierChangeCommand, GuardianIncidentDto?>
{
    private readonly IWaIntelDbContext _db;
    private readonly IEventBusPublisher _publisher;

    public RecordTierChangeHandler(IWaIntelDbContext db, IEventBusPublisher publisher)
    {
        _db = db;
        _publisher = publisher;
    }

    public async Task<GuardianIncidentDto?> HandleAsync(RecordTierChangeCommand command, CancellationToken cancellationToken)
    {
        await _db.SetTenantContextAsync(command.TenantId, cancellationToken);

        var phoneNumber = await _db.WabaPhoneNumbers
            .FirstOrDefaultAsync(p => p.Id == command.PhoneNumberId, cancellationToken);
        if (phoneNumber is null)
        {
            return null;
        }

        var effectiveOldRaw = phoneNumber.MessagingTier ?? command.RawOldTier;

        if (string.Equals(effectiveOldRaw, command.RawNewTier, StringComparison.OrdinalIgnoreCase))
        {
            return null; // no real change
        }

        if (!QualityCodes.TryNormalizeTier(command.RawNewTier, out var canonicalNewTier))
        {
            // Still store Meta's raw code verbatim (issue #19 convention) even though we can't
            // write a CHECK-constrained event row for a tier code the platform doesn't recognize.
            phoneNumber.MessagingTier = command.RawNewTier;
            phoneNumber.UpdatedAt = DateTimeOffset.UtcNow;
            phoneNumber.Version += 1;
            // Unrecognized tier code: no quality.messaging_tier_events row (its CHECK only
            // allows the known canonical set) — the raw value is still preserved on
            // waba.phone_numbers above, matching issue #19's "store whatever Meta sends" rule.
            await _db.SaveChangesAsync(cancellationToken);
            return null;
        }

        var canonicalOldTier = QualityCodes.TryNormalizeTier(effectiveOldRaw, out var oldTier) ? oldTier : null;
        var now = DateTimeOffset.UtcNow;

        _db.MessagingTierEvents.Add(new MessagingTierEvent
        {
            Id = Guid.NewGuid(),
            TenantId = command.TenantId,
            PhoneNumberId = command.PhoneNumberId,
            OldTier = canonicalOldTier,
            NewTier = canonicalNewTier,
            EventSource = command.EventSource,
            Payload = command.RawPayload,
            OccurredAt = now,
            CreatedAt = now,
        });

        phoneNumber.MessagingTier = command.RawNewTier;
        phoneNumber.UpdatedAt = now;
        phoneNumber.Version += 1;

        GuardianIncidentDto? result = null;

        if (TierRules.IsDowngrade(canonicalOldTier, canonicalNewTier))
        {
            var existing = await _db.GuardianIncidents.FirstOrDefaultAsync(
                i => i.PhoneNumberId == command.PhoneNumberId
                  && i.IncidentType == GuardianRules.IncidentTierDowngrade
                  && i.Status != GuardianRules.StatusResolved,
                cancellationToken);

            if (existing is null)
            {
                var incident = new GuardianIncident
                {
                    Id = Guid.NewGuid(),
                    TenantId = command.TenantId,
                    PhoneNumberId = command.PhoneNumberId,
                    IncidentType = GuardianRules.IncidentTierDowngrade,
                    Severity = GuardianRules.DetermineSeverity(GuardianRules.IncidentTierDowngrade),
                    Status = GuardianRules.StatusOpen,
                    ThrottleAction = GuardianRules.ThrottleNone,
                    TriggerRating = null,
                    OpenedAt = now,
                    CreatedAt = now,
                    UpdatedAt = now,
                    Version = 1,
                };
                _db.GuardianIncidents.Add(incident);
                result = new GuardianIncidentDto(
                    incident.Id, incident.PhoneNumberId, incident.IncidentType, incident.Severity,
                    incident.Status, incident.ThrottleAction, incident.TriggerRating, incident.OpenedAt, incident.ResolvedAt);

                await _db.SaveChangesAsync(cancellationToken);

                await _publisher.PublishAsync(new GuardianIncidentOpenedV1
                {
                    TenantId = command.TenantId,
                    PhoneNumberId = command.PhoneNumberId,
                    IncidentId = incident.Id,
                    IncidentType = incident.IncidentType,
                    Severity = incident.Severity,
                    ThrottleAction = incident.ThrottleAction,
                }, cancellationToken);

                return result;
            }

            result = new GuardianIncidentDto(
                existing.Id, existing.PhoneNumberId, existing.IncidentType, existing.Severity,
                existing.Status, existing.ThrottleAction, existing.TriggerRating, existing.OpenedAt, existing.ResolvedAt);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return result;
    }
}
