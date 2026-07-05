using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WaBilling.Application.Common.Interfaces;
using WaBilling.Application.Costs.Commands.RecordMessageCost;
using WaPlatform.IntegrationTests.Support;
using Xunit;

namespace WaPlatform.IntegrationTests.Billing;

/// <summary>
/// <see cref="RecordMessageCostCommandHandler"/> (issue #19) is a deliberate check-then-insert
/// idempotency pattern whose OWN doc comment says the DB's <c>message_costs_wamid_key</c> UNIQUE
/// constraint is "defense in depth" for a genuinely concurrent redelivery — i.e. the class is
/// explicitly designed around a real Postgres constraint that EF Core's InMemory provider (used by
/// every WaBilling.Tests unit test, per InMemoryWaBillingDbContext's own doc comment: "no
/// OnModelCreating override... doesn't enforce RLS or the real DB's UNIQUE constraints") cannot
/// enforce the same way — every existing unit test only ever exercises the handler's
/// pre-check-then-insert FAST path. This test races two independent handler instances (two
/// DbContexts/connections, not two threads sharing one) against the same wamid for the SAME
/// tenant (a redelivered webhook is always the same tenant — a cross-tenant collision on the same
/// wamid isn't a real scenario, and would also confuse the handler's own duplicate re-check, which
/// is itself RLS-scoped) and proves the loser's <c>DbUpdateException</c> is caught and resolved to
/// "duplicate, skip" rather than surfacing to the caller or double-billing the ledger.
/// </summary>
[Collection("IntegrationTests")]
public sealed class RecordMessageCostIdempotencyTests
{
    private readonly DatabaseFixture _fixture;

    public RecordMessageCostIdempotencyTests(DatabaseFixture fixture) => _fixture = fixture;

    [RequiresDockerFact]
    public async Task HandleAsync_ConcurrentDuplicateWamid_LoserCatchesRealUniqueConstraintInsteadOfDoubleBilling()
    {
        var tenantId = Guid.NewGuid();
        var businessAccountId = Guid.NewGuid();
        var phoneNumberId = Guid.NewGuid();
        var wamid = $"wamid.itest-{Guid.NewGuid():N}";

        await SqlSeeding.SeedTenantAsync(_fixture.AdminConnectionString, tenantId, $"it-{tenantId:N}"[..18]);
        await SqlSeeding.SeedBusinessAccountAsync(_fixture.AdminConnectionString, businessAccountId, tenantId, $"waba-{businessAccountId:N}"[..18]);
        await SqlSeeding.SeedPhoneNumberAsync(_fixture.AdminConnectionString, phoneNumberId, tenantId, businessAccountId, $"meta-{phoneNumberId:N}"[..15]);

        // Pre-seed BOTH usage_counters rows (the handler upserts "utility" AND "all" per call) so
        // the two concurrent calls each UPDATE the same already-existing row rather than racing to
        // INSERT a new one — that would trip usage_counters_tenant_id_category_period_start_key
        // and mask the ONE constraint this test exists to exercise. See SeedUsageCounterAsync's
        // doc comment.
        await SqlSeeding.SeedUsageCounterAsync(_fixture.AdminConnectionString, tenantId, "utility");
        await SqlSeeding.SeedUsageCounterAsync(_fixture.AdminConnectionString, tenantId, "all");

        var (providerA, tenantForA) = TestHost.BuildBillingProvider(_fixture.AppConnectionString);
        var (providerB, tenantForB) = TestHost.BuildBillingProvider(_fixture.AppConnectionString);
        await using var disposeA = providerA;
        await using var disposeB = providerB;
        tenantForA.TenantId = tenantId;
        tenantForB.TenantId = tenantId;

        await using var scopeA = providerA.CreateAsyncScope();
        await using var scopeB = providerB.CreateAsyncScope();
        var dbA = scopeA.ServiceProvider.GetRequiredService<IWaBillingDbContext>();
        var dbB = scopeB.ServiceProvider.GetRequiredService<IWaBillingDbContext>();

        var handlerA = new RecordMessageCostCommandHandler(dbA, NullLogger<RecordMessageCostCommandHandler>.Instance);
        var handlerB = new RecordMessageCostCommandHandler(dbB, NullLogger<RecordMessageCostCommandHandler>.Instance);

        var command = new RecordMessageCostCommand(
            TenantId: tenantId, PhoneNumberId: phoneNumberId, Wamid: wamid,
            PricingCategory: "utility", PricingModel: "CBP", Billable: true,
            Amount: 0.50m, Currency: "INR", DestinationMarket: "IN",
            PricingRawJson: "{\"category\":\"utility\"}");

        // GENUINELY concurrent (Task.WhenAll, two independent DbContexts/connections) — BOTH
        // handlers' AnyAsync pre-checks can legitimately return false (neither has committed yet)
        // and both attempt the insert. The real message_costs_wamid_key UNIQUE constraint, not the
        // handler's own pre-check, is what forces exactly one winner; the loser's handler must
        // catch that DbUpdateException and resolve it to "duplicate, skip" rather than letting it
        // propagate to the caller (which would look like a crashed consumer, not a no-op).
        var taskA = handlerA.HandleAsync(command, CancellationToken.None);
        var taskB = handlerB.HandleAsync(command, CancellationToken.None);
        var results = await Task.WhenAll(taskA, taskB); // must not throw for either

        Assert.True(results[0] ^ results[1], "exactly one of the two racing inserts should have recorded the ledger row");

        var rowCount = await SqlSeeding.CountMessageCostsByWamidAsync(_fixture.AdminConnectionString, wamid);
        Assert.Equal(1, rowCount);
    }
}
