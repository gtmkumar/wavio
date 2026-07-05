using WaIntel.Application.Common.Interfaces;
using WaIntel.Application.Quality.Dtos;
using WaIntel.Application.Quality.Logic;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.SharedDataModel.Entities.Quality;
using WaPlatform.Contracts.IntegrationEvents.V1;
using Microsoft.EntityFrameworkCore;

namespace WaIntel.Application.Quality.Commands.RecordQualityChange;

/// <summary>
/// Applies a quality-rating change: writes the append-only <c>number_quality_events</c> row,
/// updates the current rating on <c>waba.phone_numbers</c>, and opens/resolves the corresponding
/// <c>guardian_incidents</c> row per <see cref="GuardianRules"/> (issue #20, spec §4.6).
///
/// The event's own <c>PreviousRating</c> (from <c>QualityChangedV1</c>) is NOT trusted as the
/// diff baseline — WaIngest's normalizer is stateless and always reports it as "UNKNOWN" (see
/// that class's doc comment: "Wave 2 (#20 Guardian) owns that state"). This handler reads the
/// number's CURRENTLY STORED rating instead, which is the real previous value.
///
/// Idempotent by design, not by an event dedupe table (same convention as
/// <c>MessageReceivedConsumerService</c>): a redelivered webhook reporting the SAME rating the
/// number already has is a no-op — no duplicate event row, no duplicate incident.
/// </summary>
public sealed class RecordQualityChangeHandler : ICommandHandler<RecordQualityChangeCommand, GuardianIncidentDto?>
{
    private readonly IWaIntelDbContext _db;
    private readonly IEventBusPublisher _publisher;

    public RecordQualityChangeHandler(IWaIntelDbContext db, IEventBusPublisher publisher)
    {
        _db = db;
        _publisher = publisher;
    }

    public async Task<GuardianIncidentDto?> HandleAsync(RecordQualityChangeCommand command, CancellationToken cancellationToken)
    {
        await _db.SetTenantContextAsync(command.TenantId, cancellationToken);

        var phoneNumber = await _db.WabaPhoneNumbers
            .FirstOrDefaultAsync(p => p.Id == command.PhoneNumberId, cancellationToken);
        if (phoneNumber is null)
        {
            // Resolved via ITenantResolver, which itself queries waba.phone_numbers — this
            // "shouldn't happen" (same defensive posture as WindowClosingScannerService.
            // ScanTenantAsync's orphan guard). Nothing to update; not an error worth throwing.
            return null;
        }

        var oldRating = QualityCodes.NormalizeRating(phoneNumber.QualityRating);
        var newRating = QualityCodes.NormalizeRating(command.RawNewRating);

        if (oldRating == newRating)
        {
            // No real change (redelivery, or a simulate call re-asserting the current state) —
            // return whatever incident is already open for context, write nothing new.
            return await FindOpenIncidentDtoAsync(command.PhoneNumberId, cancellationToken);
        }

        var now = DateTimeOffset.UtcNow;

        _db.NumberQualityEvents.Add(new NumberQualityEvent
        {
            Id = Guid.NewGuid(),
            TenantId = command.TenantId,
            PhoneNumberId = command.PhoneNumberId,
            OldRating = oldRating,
            NewRating = newRating,
            EventSource = command.EventSource,
            Payload = command.RawPayload,
            OccurredAt = now,
            CreatedAt = now,
        });

        phoneNumber.QualityRating = QualityCodes.ToPhoneNumberRatingColumn(newRating);
        phoneNumber.UpdatedAt = now;
        phoneNumber.Version += 1;

        GuardianIncidentDto? result = null;
        var incidentType = GuardianRules.DetermineIncidentType(newRating);

        if (incidentType is not null)
        {
            // Only one quality incident open at a time per number — resolve the OTHER quality
            // type first (e.g. red -> yellow resolves quality_red, then opens/keeps quality_yellow).
            await ResolveOpenIncidentsAsync(
                command.PhoneNumberId,
                GuardianRules.QualityIncidentTypes.Where(t => t != incidentType),
                now, cancellationToken);

            var existing = await _db.GuardianIncidents.FirstOrDefaultAsync(
                i => i.PhoneNumberId == command.PhoneNumberId
                  && i.IncidentType == incidentType
                  && i.Status != GuardianRules.StatusResolved,
                cancellationToken);

            if (existing is null)
            {
                var incident = new GuardianIncident
                {
                    Id = Guid.NewGuid(),
                    TenantId = command.TenantId,
                    PhoneNumberId = command.PhoneNumberId,
                    IncidentType = incidentType,
                    Severity = GuardianRules.DetermineSeverity(incidentType),
                    Status = GuardianRules.StatusOpen,
                    ThrottleAction = GuardianRules.DetermineThrottleAction(incidentType),
                    TriggerRating = newRating,
                    OpenedAt = now,
                    CreatedAt = now,
                    UpdatedAt = now,
                    Version = 1,
                };
                _db.GuardianIncidents.Add(incident);
                result = ToDto(incident);

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

            result = ToDto(existing);
        }
        else if (GuardianRules.ShouldResolveOnRecovery(newRating))
        {
            await ResolveOpenIncidentsAsync(
                command.PhoneNumberId, GuardianRules.QualityIncidentTypes, now, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return result;
    }

    /// <summary>Resolves every currently-open incident of the given types for this number and
    /// publishes a resolution event per incident. Returns before <c>SaveChangesAsync</c> — caller
    /// commits alongside its own other writes in the same unit of work.</summary>
    private async Task ResolveOpenIncidentsAsync(
        Guid phoneNumberId, IEnumerable<string> incidentTypes, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var typesToResolve = incidentTypes as ICollection<string> ?? incidentTypes.ToList();
        if (typesToResolve.Count == 0) return;

        var openIncidents = await _db.GuardianIncidents
            .Where(i => i.PhoneNumberId == phoneNumberId
                     && typesToResolve.Contains(i.IncidentType)
                     && i.Status != GuardianRules.StatusResolved)
            .ToListAsync(cancellationToken);

        foreach (var incident in openIncidents)
        {
            incident.Status = GuardianRules.StatusResolved;
            incident.ResolvedAt = now;
            incident.UpdatedAt = now;
            incident.Version += 1;
        }

        if (openIncidents.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            foreach (var incident in openIncidents)
            {
                await _publisher.PublishAsync(new GuardianIncidentResolvedV1
                {
                    TenantId = incident.TenantId,
                    PhoneNumberId = incident.PhoneNumberId,
                    IncidentId = incident.Id,
                    IncidentType = incident.IncidentType,
                }, cancellationToken);
            }
        }
    }

    private async Task<GuardianIncidentDto?> FindOpenIncidentDtoAsync(Guid phoneNumberId, CancellationToken cancellationToken)
    {
        var incident = await _db.GuardianIncidents
            .AsNoTracking()
            .Where(i => i.PhoneNumberId == phoneNumberId && i.Status != GuardianRules.StatusResolved)
            .OrderByDescending(i => i.OpenedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return incident is null ? null : ToDto(incident);
    }

    private static GuardianIncidentDto ToDto(GuardianIncident incident) => new(
        incident.Id, incident.PhoneNumberId, incident.IncidentType, incident.Severity,
        incident.Status, incident.ThrottleAction, incident.TriggerRating, incident.OpenedAt, incident.ResolvedAt);
}
