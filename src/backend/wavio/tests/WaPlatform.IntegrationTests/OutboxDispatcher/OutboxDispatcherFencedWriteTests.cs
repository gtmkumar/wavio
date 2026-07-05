using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WaGateway.Infrastructure.BackgroundWork;
using WaGateway.Infrastructure.Graph;
using WaGateway.Infrastructure.RateLimiting;
using WaPlatform.IntegrationTests.Support;
using Xunit;

namespace WaPlatform.IntegrationTests.OutboxDispatcher;

/// <summary>
/// Covers the headline gap QA flagged for PR #45 (issue #14):
/// <c>WaGateway.Infrastructure.BackgroundWork.OutboxDispatcherService</c> has zero automated
/// coverage because every state-transition write is a fenced <c>ExecuteUpdateAsync(...)</c>, which
/// throws against EF Core's InMemory provider (see the class's own XML doc comment and
/// .claude/agent-memory/qa-test-engineer/review-pr45-gateway-send.md). This test drives the REAL
/// <see cref="OutboxDispatcherService.LeaseNextBatchAsync"/> and
/// <see cref="OutboxDispatcherService.ProcessEntryAsync"/> (made <c>internal</c> for this project
/// via InternalsVisibleTo — see WaGateway.Infrastructure.csproj) against real Postgres, simulating
/// exactly the race PR #45's S1 fix (fenced writes) exists to close: a Graph call slow enough for
/// its lease to be reclaimed as stale while still in flight.
/// </summary>
[Collection("IntegrationTests")]
public sealed class OutboxDispatcherFencedWriteTests
{
    private readonly DatabaseFixture _fixture;

    public OutboxDispatcherFencedWriteTests(DatabaseFixture fixture) => _fixture = fixture;

    [RequiresDockerFact]
    public async Task ProcessEntryAsync_LeaseStolenWhileGraphCallInFlight_DiscardsResultAndDoesNotDuplicateDispatch()
    {
        var tenantId = Guid.NewGuid();
        var businessAccountId = Guid.NewGuid();
        var phoneNumberId = Guid.NewGuid();

        await SqlSeeding.SeedTenantAsync(_fixture.AdminConnectionString, tenantId, $"it-{tenantId:N}"[..18]);
        await SqlSeeding.SeedBusinessAccountAsync(_fixture.AdminConnectionString, businessAccountId, tenantId, $"waba-{businessAccountId:N}"[..18]);
        await SqlSeeding.SeedPhoneNumberAsync(_fixture.AdminConnectionString, phoneNumberId, tenantId, businessAccountId, $"meta-{phoneNumberId:N}"[..15]);
        var outboxEntryId = await SqlSeeding.SeedAcceptedMessageWithOutboxEntryAsync(_fixture.AdminConnectionString, tenantId, phoneNumberId);

        await using var providerA = TestHost.BuildGatewayProvider(_fixture.AppConnectionString);
        await using var providerB = TestHost.BuildGatewayProvider(_fixture.AppConnectionString);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Outbox:PollIntervalMs"] = "1000",
                ["Outbox:StaleLockSeconds"] = "5",
                ["Outbox:BatchSize"] = "20",
            })
            .Build();

        var graphClientA = new ControllableGraphClient();
        var graphClientB = new ControllableGraphClient();
        graphClientB.Release(); // B never needs to pause — only A's call simulates being in flight.

        var dispatcherA = NewDispatcher(providerA, graphClientA, config);
        var dispatcherB = NewDispatcher(providerB, graphClientB, config);

        // Instance A claims the lease.
        var claimedByA = await dispatcherA.LeaseNextBatchAsync(CancellationToken.None);
        Assert.Equal([outboxEntryId], claimedByA);

        // Start A's processing — its Graph call blocks on the gate, simulating an in-flight send.
        var processTaskA = dispatcherA.ProcessEntryAsync(outboxEntryId, CancellationToken.None);
        await graphClientA.CallStarted;

        // Instance B steals the lease: back-date locked_at past StaleLockSeconds, then let B's own
        // real reclaim logic (LeaseNextBatchAsync, not test-only SQL) claim it — the exact
        // "slow call outlives the reclaim window" scenario PR #45's S1 fix targets.
        await SqlSeeding.BackdateOutboxLockAsync(_fixture.AdminConnectionString, outboxEntryId, TimeSpan.FromSeconds(30));
        var claimedByB = await dispatcherB.LeaseNextBatchAsync(CancellationToken.None);
        Assert.Equal([outboxEntryId], claimedByB);
        Assert.NotEqual(dispatcherA.InstanceId, dispatcherB.InstanceId);

        // Now let A's Graph call "succeed" — its fenced completion write must affect 0 rows and
        // discard, NOT overwrite B's lease/state.
        graphClientA.Release();
        await processTaskA;

        var afterA = await SqlSeeding.ReadOutboxAndMessageStateAsync(_fixture.AdminConnectionString, outboxEntryId);
        Assert.Equal("dispatching", afterA.Status); // still B's claim, never flipped to "dispatched" by A
        Assert.Equal(dispatcherB.InstanceId, afterA.LockedBy);
        Assert.Null(afterA.Wamid); // A's discarded write never touched outbound_messages
        Assert.Equal("accepted", afterA.MessageStatus);

        // B completes its own (real) processing — exactly one terminal dispatch, no duplicate send.
        await dispatcherB.ProcessEntryAsync(outboxEntryId, CancellationToken.None);

        var afterB = await SqlSeeding.ReadOutboxAndMessageStateAsync(_fixture.AdminConnectionString, outboxEntryId);
        Assert.Equal("dispatched", afterB.Status);
        Assert.Equal(dispatcherB.InstanceId, afterB.LockedBy);
        Assert.NotNull(afterB.Wamid);
        Assert.Equal("dispatched", afterB.MessageStatus);
    }

    private static OutboxDispatcherService NewDispatcher(
        IServiceProvider provider, ControllableGraphClient graphClient, IConfiguration config) =>
        new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            graphClient,
            new TokenBucketRateLimiter(NullLogger<TokenBucketRateLimiter>.Instance),
            new MessagingTierGate(NullLogger<MessagingTierGate>.Instance),
            new GuardianThrottleGate(NullLogger<GuardianThrottleGate>.Instance),
            Options.Create(new MetaGraphOptions { DefaultThroughputPerSecond = 1000, DefaultMessagingTierPerDay = 0 }),
            config,
            NullLogger<OutboxDispatcherService>.Instance);
}
