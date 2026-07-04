using WaIntel.Application.Windows.Commands.UpsertWindowOnMessageReceived;
using WaIntel.Application.Windows.Queries.GetWindowState;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace WaIntel.Tests.Windows;

public class GetWindowStateHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid PhoneNumberId = Guid.NewGuid();
    private const string UserWaId = "919876543210";

    private static IMemoryCache NewCache() => new MemoryCache(new MemoryCacheOptions());

    [Fact]
    public async Task Returns_null_when_no_window_exists_and_does_not_cache_the_miss()
    {
        await using var db = InMemoryWaIntelDbContext.Create(nameof(Returns_null_when_no_window_exists_and_does_not_cache_the_miss));
        var cache = NewCache();
        var handler = new GetWindowStateHandler(db, cache);

        var result = await handler.HandleAsync(
            new GetWindowStateQuery(TenantId, UserWaId, null), CancellationToken.None);

        Assert.Null(result);
        Assert.False(cache.TryGetValue(GetWindowStateHandler.BuildCacheKey(TenantId, UserWaId), out _));
    }

    [Fact]
    public async Task Returns_the_window_state_and_populates_the_cache_on_a_miss()
    {
        await using var db = InMemoryWaIntelDbContext.Create(nameof(Returns_the_window_state_and_populates_the_cache_on_a_miss));
        var upsertHandler = new UpsertWindowOnMessageReceivedHandler(db);
        await upsertHandler.HandleAsync(
            new UpsertWindowOnMessageReceivedCommand(TenantId, PhoneNumberId, UserWaId, DateTimeOffset.UtcNow, true, null),
            CancellationToken.None);

        var cache = NewCache();
        var handler = new GetWindowStateHandler(db, cache);

        var result = await handler.HandleAsync(
            new GetWindowStateQuery(TenantId, UserWaId, null), CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(cache.TryGetValue(GetWindowStateHandler.BuildCacheKey(TenantId, UserWaId), out _));
    }

    [Fact]
    public async Task A_cache_hit_never_touches_the_database()
    {
        await using var db = InMemoryWaIntelDbContext.Create(nameof(A_cache_hit_never_touches_the_database));
        var cache = NewCache();
        var handler = new GetWindowStateHandler(db, cache);
        var cacheKey = GetWindowStateHandler.BuildCacheKey(TenantId, UserWaId);

        var seeded = new WaIntel.Application.Windows.Dtos.WindowStateDto(
            UserWaId, PhoneNumberId, "organic", DateTimeOffset.UtcNow.AddHours(20), true, null, false);
        cache.Set(cacheKey, seeded);

        // No ConversationWindow row exists in the DB at all — if the handler fell through to the
        // DB it would return null, not the seeded value. It must return the cached value instead.
        var result = await handler.HandleAsync(
            new GetWindowStateQuery(TenantId, UserWaId, null), CancellationToken.None);

        Assert.Same(seeded, result);
    }

    [Fact]
    public async Task Removing_the_cache_entry_forces_a_fresh_DB_read_reflecting_the_latest_state()
    {
        // This is what WindowCacheInvalidationListener does on a NOTIFY — proves the
        // "invalidate then re-read" half of the fast-lookup contract without needing a live
        // Postgres LISTEN/NOTIFY round trip.
        await using var db = InMemoryWaIntelDbContext.Create(nameof(Removing_the_cache_entry_forces_a_fresh_DB_read_reflecting_the_latest_state));
        var upsertHandler = new UpsertWindowOnMessageReceivedHandler(db);
        var cache = NewCache();
        var queryHandler = new GetWindowStateHandler(db, cache);
        var cacheKey = GetWindowStateHandler.BuildCacheKey(TenantId, UserWaId);

        var firstSentAt = DateTimeOffset.UtcNow;
        await upsertHandler.HandleAsync(
            new UpsertWindowOnMessageReceivedCommand(TenantId, PhoneNumberId, UserWaId, firstSentAt, true, null),
            CancellationToken.None);
        var firstRead = await queryHandler.HandleAsync(new GetWindowStateQuery(TenantId, UserWaId, null), CancellationToken.None);
        Assert.Equal(firstSentAt + TimeSpan.FromHours(24), firstRead!.CsExpiresAt);

        // Window is extended by a new inbound message — the handler itself clears nothing in the
        // cache (that's the listener's job); simulate the listener having done so.
        var secondSentAt = firstSentAt + TimeSpan.FromHours(5);
        await upsertHandler.HandleAsync(
            new UpsertWindowOnMessageReceivedCommand(TenantId, PhoneNumberId, UserWaId, secondSentAt, true, null),
            CancellationToken.None);
        cache.Remove(cacheKey);

        var secondRead = await queryHandler.HandleAsync(new GetWindowStateQuery(TenantId, UserWaId, null), CancellationToken.None);

        Assert.Equal(secondSentAt + TimeSpan.FromHours(24), secondRead!.CsExpiresAt);
    }
}
