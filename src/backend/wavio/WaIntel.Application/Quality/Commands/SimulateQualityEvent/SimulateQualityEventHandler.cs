using WaIntel.Application.Common.Interfaces;
using WaIntel.Application.Quality.Commands.RecordQualityChange;
using WaIntel.Application.Quality.Commands.RecordTierChange;
using WaIntel.Application.Quality.Dtos;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.Extensions.Hosting;

namespace WaIntel.Application.Quality.Commands.SimulateQualityEvent;

/// <summary>
/// Reuses <see cref="RecordQualityChangeHandler"/>/<see cref="RecordTierChangeHandler"/> directly
/// (plain composition, not the dispatcher — this handler already IS inside a dispatch) rather than
/// duplicating their read-current-state / diff / write-event / open-incident logic. Same
/// double-gate "never in Production" pattern as <c>SimulateWindowHandler</c> (issue #15): the
/// WebApi endpoint's route is only mapped outside Production AND this handler independently
/// refuses too.
/// </summary>
public sealed class SimulateQualityEventHandler : ICommandHandler<SimulateQualityEventCommand, GuardianIncidentDto?>
{
    private readonly IWaIntelDbContext _db;
    private readonly IEventBusPublisher _publisher;
    private readonly IHostEnvironment _environment;

    public SimulateQualityEventHandler(IWaIntelDbContext db, IEventBusPublisher publisher, IHostEnvironment environment)
    {
        _db = db;
        _publisher = publisher;
        _environment = environment;
    }

    public async Task<GuardianIncidentDto?> HandleAsync(SimulateQualityEventCommand command, CancellationToken cancellationToken)
    {
        if (_environment.IsProduction())
        {
            throw new InvalidOperationException(
                "SimulateQualityEventCommand must never run in Production — this is the second, " +
                "independent gate (the WebApi endpoint's own environment check is the first).");
        }

        await _db.SetTenantContextAsync(command.TenantId, cancellationToken);

        GuardianIncidentDto? result = null;

        if (!string.IsNullOrWhiteSpace(command.SimulatedRating))
        {
            var qualityHandler = new RecordQualityChangeHandler(_db, _publisher);
            result = await qualityHandler.HandleAsync(
                new RecordQualityChangeCommand(
                    command.TenantId, command.PhoneNumberId, WabaId: "simulated",
                    command.SimulatedRating, EventSource: "simulated", RawPayload: null),
                cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(command.SimulatedTier))
        {
            var tierHandler = new RecordTierChangeHandler(_db, _publisher);
            var tierResult = await tierHandler.HandleAsync(
                new RecordTierChangeCommand(
                    command.TenantId, command.PhoneNumberId, WabaId: "simulated",
                    RawOldTier: null, command.SimulatedTier, EventSource: "simulated", RawPayload: null),
                cancellationToken);
            result ??= tierResult;
        }

        return result;
    }
}
