using WaIntel.Application.Common.Interfaces;
using WaIntel.Application.Windows.Dtos;
using WaIntel.Application.Windows.Logic;
using Wavio.Utilities.CQRS.Abstractions;
using wavio.SharedDataModel.Entities.Sessions;
using Microsoft.EntityFrameworkCore;

namespace WaIntel.Application.Windows.Commands.UpsertWindowOnMessageReceived;

public sealed class UpsertWindowOnMessageReceivedHandler
    : ICommandHandler<UpsertWindowOnMessageReceivedCommand, WindowStateDto>
{
    private readonly IWaIntelDbContext _db;

    public UpsertWindowOnMessageReceivedHandler(IWaIntelDbContext db) => _db = db;

    public async Task<WindowStateDto> HandleAsync(
        UpsertWindowOnMessageReceivedCommand command, CancellationToken cancellationToken)
    {
        // Defensive, not just for the write: the SELECT below is RLS-scoped too, and this
        // handler is invoked both from an HTTP request (ICurrentTenant already correct) and
        // from the background RabbitMQ consumer (no HttpContext, so ICurrentTenant.TenantId is
        // null and the interceptor's connection-open GUC set is empty) — see
        // IWaIntelDbContext.SetTenantContextAsync's doc comment for why this is needed at all.
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
                Origin = "organic",
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1
            };
            _db.ConversationWindows.Add(window);
        }

        var newEvents = new List<WindowEvent>();

        if (command.OpensCustomerServiceWindow)
        {
            var wasOpen = WindowRules.IsOpen(window.CsExpiresAt, now);
            var newExpiry = WindowRules.CalculateCsExpiry(command.SentAt);

            newEvents.Add(new WindowEvent
            {
                EventType = wasOpen ? "cs_extended" : "cs_opened",
                OldExpiresAt = window.CsExpiresAt,
                NewExpiresAt = newExpiry
            });

            window.CsExpiresAt = newExpiry;
            window.CsLastInboundAt = command.SentAt;
            // Re-opened/extended window — clear the guard so it gets a fresh closing notification
            // (db/migrations/V008 column comment on closing_notified_at).
            window.ClosingNotifiedAt = null;
        }

        if (command.CtwaReferralJson is not null)
        {
            var wasOpen = WindowRules.IsOpen(window.CtwaExpiresAt, now);
            var newExpiry = WindowRules.CalculateCtwaExpiry(command.SentAt);

            newEvents.Add(new WindowEvent
            {
                EventType = wasOpen ? "ctwa_extended" : "ctwa_opened",
                OldExpiresAt = window.CtwaExpiresAt,
                NewExpiresAt = newExpiry
            });

            window.CtwaExpiresAt = newExpiry;
            window.CtwaEnteredAt = command.SentAt;
            window.CtwaReferral = command.CtwaReferralJson;
            window.Origin = "ctwa";
            window.ClosingNotifiedAt = null;
        }

        window.UpdatedAt = now;
        window.Version += 1;

        foreach (var windowEvent in newEvents)
        {
            windowEvent.Id = Guid.NewGuid();
            windowEvent.TenantId = command.TenantId;
            windowEvent.ConversationWindowId = window.Id;
            windowEvent.OccurredAt = now;
            windowEvent.CreatedAt = now;
            _db.WindowEvents.Add(windowEvent);
        }

        await _db.SaveChangesAsync(cancellationToken);

        // Fast-lookup cache invalidation (LISTEN/NOTIFY, "DocSlot RBAC resolver pattern") — fired
        // AFTER the row change is committed, so a listener that wakes up on this notification and
        // re-reads the row always sees the new state, never a stale one racing the commit.
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
