using WaIntel.Application.Common.Interfaces;
using WaIntel.Application.Windows.Dtos;
using WaIntel.Application.Windows.Logic;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.SharedDataModel.Entities.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace WaIntel.Application.Windows.Commands.SimulateWindow;

/// <summary>
/// Independently refuses in Production even though the WebApi endpoint is also gated
/// (fail-closed, defense in depth — a route mis-registration must not be the only thing
/// standing between this and a live tenant's data). <see cref="IsSimulated"/> = true is the
/// permanent record on the row that it was fabricated, per db/migrations/V008's column comment.
/// </summary>
public sealed class SimulateWindowHandler : ICommandHandler<SimulateWindowCommand, WindowStateDto>
{
    private readonly IWaIntelDbContext _db;
    private readonly IHostEnvironment _environment;

    public SimulateWindowHandler(IWaIntelDbContext db, IHostEnvironment environment)
    {
        _db = db;
        _environment = environment;
    }

    public async Task<WindowStateDto> HandleAsync(SimulateWindowCommand command, CancellationToken cancellationToken)
    {
        if (_environment.IsProduction())
        {
            throw new InvalidOperationException(
                "SimulateWindowCommand must never run in Production — this is the second, " +
                "independent gate (the WebApi endpoint's own environment check is the first).");
        }

        // Defensive: see IWaIntelDbContext.SetTenantContextAsync's doc comment — this endpoint is
        // always HTTP-triggered so ICurrentTenant is already correct, but setting it explicitly
        // here too costs nothing and keeps every write handler uniformly self-sufficient.
        await _db.SetTenantContextAsync(command.TenantId, cancellationToken);

        var now = DateTimeOffset.UtcNow;

        var window = await _db.ConversationWindows.SingleOrDefaultAsync(
            w => w.TenantId == command.TenantId
              && w.PhoneNumberId == command.PhoneNumberId
              && w.UserWaId == command.UserWaId,
            cancellationToken);

        if (window is null)
        {
            window = new ConversationWindow
            {
                Id = Guid.NewGuid(),
                TenantId = command.TenantId,
                PhoneNumberId = command.PhoneNumberId,
                UserWaId = command.UserWaId,
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1
            };
            _db.ConversationWindows.Add(window);
        }

        window.Origin = command.Origin;
        window.CsExpiresAt = command.CsExpiresAt;
        window.CtwaExpiresAt = command.CtwaExpiresAt;
        window.IsSimulated = true;
        window.ClosingNotifiedAt = null; // fresh simulated state — eligible for a closing scan same as a real window
        window.UpdatedAt = now;
        window.Version += 1;

        _db.WindowEvents.Add(new WindowEvent
        {
            Id = Guid.NewGuid(),
            TenantId = command.TenantId,
            ConversationWindowId = window.Id,
            EventType = "simulated",
            NewExpiresAt = command.CsExpiresAt ?? command.CtwaExpiresAt,
            OccurredAt = now,
            CreatedAt = now
        });

        await _db.SaveChangesAsync(cancellationToken);

        var notifyKey = $"{command.TenantId}:{command.PhoneNumberId}:{command.UserWaId}";
        await _db.NotifyAsync("conversation_window_changed", notifyKey, cancellationToken);

        return new WindowStateDto(
            window.UserWaId,
            window.PhoneNumberId,
            window.Origin,
            window.CsExpiresAt,
            WindowRules.IsOpen(window.CsExpiresAt, now),
            window.CtwaExpiresAt,
            WindowRules.IsOpen(window.CtwaExpiresAt, now));
    }
}
