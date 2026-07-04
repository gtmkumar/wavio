using WaIntel.Application.Windows.Commands.UpsertWindowOnMessageReceived;
using Xunit;

namespace WaIntel.Tests.Windows;

public class UpsertWindowOnMessageReceivedHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid PhoneNumberId = Guid.NewGuid();
    private const string UserWaId = "919876543210";

    [Fact]
    public async Task First_message_creates_a_new_organic_window_with_a_24h_CS_expiry()
    {
        await using var db = InMemoryWaIntelDbContext.Create(nameof(First_message_creates_a_new_organic_window_with_a_24h_CS_expiry));
        var handler = new UpsertWindowOnMessageReceivedHandler(db);
        var sentAt = DateTimeOffset.UtcNow;

        var result = await handler.HandleAsync(
            new UpsertWindowOnMessageReceivedCommand(TenantId, PhoneNumberId, UserWaId, sentAt, true, null),
            CancellationToken.None);

        Assert.Equal("organic", result.Origin);
        Assert.True(result.CsOpen);
        Assert.Equal(sentAt + TimeSpan.FromHours(24), result.CsExpiresAt);
        Assert.Null(result.CtwaExpiresAt);
        Assert.False(result.CtwaOpen);
    }

    [Fact]
    public async Task A_second_message_resets_the_CS_window_to_a_fresh_24h_from_the_new_message()
    {
        await using var db = InMemoryWaIntelDbContext.Create(nameof(A_second_message_resets_the_CS_window_to_a_fresh_24h_from_the_new_message));
        var handler = new UpsertWindowOnMessageReceivedHandler(db);
        var firstMessageAt = DateTimeOffset.UtcNow;
        await handler.HandleAsync(
            new UpsertWindowOnMessageReceivedCommand(TenantId, PhoneNumberId, UserWaId, firstMessageAt, true, null),
            CancellationToken.None);

        var secondMessageAt = firstMessageAt + TimeSpan.FromHours(10);
        var result = await handler.HandleAsync(
            new UpsertWindowOnMessageReceivedCommand(TenantId, PhoneNumberId, UserWaId, secondMessageAt, true, null),
            CancellationToken.None);

        // Same row, single UPSERT target — not two windows.
        Assert.Equal(1, db.ConversationWindows.Count());
        Assert.Equal(secondMessageAt + TimeSpan.FromHours(24), result.CsExpiresAt);
    }

    [Fact]
    public async Task A_message_carrying_a_referral_opens_a_72h_CTWA_window_and_sets_origin_ctwa()
    {
        await using var db = InMemoryWaIntelDbContext.Create(nameof(A_message_carrying_a_referral_opens_a_72h_CTWA_window_and_sets_origin_ctwa));
        var handler = new UpsertWindowOnMessageReceivedHandler(db);
        var sentAt = DateTimeOffset.UtcNow;
        var referralJson = """{"source_type":"ad","source_id":"123"}""";

        var result = await handler.HandleAsync(
            new UpsertWindowOnMessageReceivedCommand(TenantId, PhoneNumberId, UserWaId, sentAt, true, referralJson),
            CancellationToken.None);

        Assert.Equal("ctwa", result.Origin);
        Assert.True(result.CtwaOpen);
        Assert.Equal(sentAt + TimeSpan.FromHours(72), result.CtwaExpiresAt);
        // The same message also refreshes the CS window — CTWA doesn't replace CS tracking.
        Assert.True(result.CsOpen);
    }

    [Fact]
    public async Task Extending_a_window_clears_the_closing_notified_guard_so_it_can_notify_again()
    {
        await using var db = InMemoryWaIntelDbContext.Create(nameof(Extending_a_window_clears_the_closing_notified_guard_so_it_can_notify_again));
        var handler = new UpsertWindowOnMessageReceivedHandler(db);
        var sentAt = DateTimeOffset.UtcNow;
        await handler.HandleAsync(
            new UpsertWindowOnMessageReceivedCommand(TenantId, PhoneNumberId, UserWaId, sentAt, true, null),
            CancellationToken.None);

        // Simulate the scanner having already claimed/notified this window.
        var window = db.ConversationWindows.Single();
        window.ClosingNotifiedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);

        await handler.HandleAsync(
            new UpsertWindowOnMessageReceivedCommand(TenantId, PhoneNumberId, UserWaId, sentAt + TimeSpan.FromHours(1), true, null),
            CancellationToken.None);

        Assert.Null(db.ConversationWindows.Single().ClosingNotifiedAt);
    }

    [Fact]
    public async Task Upserting_notifies_the_cache_invalidation_channel_with_the_tenant_phone_and_waId()
    {
        await using var db = InMemoryWaIntelDbContext.Create(nameof(Upserting_notifies_the_cache_invalidation_channel_with_the_tenant_phone_and_waId));
        var handler = new UpsertWindowOnMessageReceivedHandler(db);

        await handler.HandleAsync(
            new UpsertWindowOnMessageReceivedCommand(TenantId, PhoneNumberId, UserWaId, DateTimeOffset.UtcNow, true, null),
            CancellationToken.None);

        var notification = Assert.Single(db.Notifications);
        Assert.Equal("conversation_window_changed", notification.Channel);
        Assert.Equal($"{TenantId}:{PhoneNumberId}:{UserWaId}", notification.Payload);
    }

    [Fact]
    public async Task A_message_without_a_referral_never_touches_the_CTWA_window()
    {
        await using var db = InMemoryWaIntelDbContext.Create(nameof(A_message_without_a_referral_never_touches_the_CTWA_window));
        var handler = new UpsertWindowOnMessageReceivedHandler(db);
        var sentAt = DateTimeOffset.UtcNow;

        // First, a real CTWA entry.
        await handler.HandleAsync(
            new UpsertWindowOnMessageReceivedCommand(TenantId, PhoneNumberId, UserWaId, sentAt, true, """{"source_type":"ad"}"""),
            CancellationToken.None);

        // Then an ordinary follow-up message with no referral.
        var followUpAt = sentAt + TimeSpan.FromHours(1);
        var result = await handler.HandleAsync(
            new UpsertWindowOnMessageReceivedCommand(TenantId, PhoneNumberId, UserWaId, followUpAt, true, null),
            CancellationToken.None);

        // CTWA expiry is untouched by the follow-up (still 72h from the ORIGINAL referral entry).
        Assert.Equal(sentAt + TimeSpan.FromHours(72), result.CtwaExpiresAt);
        // But CS was refreshed to the follow-up message's own +24h.
        Assert.Equal(followUpAt + TimeSpan.FromHours(24), result.CsExpiresAt);
    }
}
