using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WaGateway.Application.Common.Interfaces;
using WaPlatform.IntegrationTests.Support;
using Xunit;

namespace WaPlatform.IntegrationTests.Rls;

/// <summary>
/// Proves the actual Postgres RLS mechanism (db/migrations/V001..V013's FORCE ROW LEVEL SECURITY
/// policies, driven through the real <c>RlsConnectionInterceptor</c> and the <c>app_user</c> role
/// — NOT the postgres superuser, which bypasses RLS regardless of policy) rather than any
/// application-level tenant filter. No unit test in this codebase can exercise this: EF Core's
/// InMemory provider has no concept of a database role or a session GUC at all.
/// </summary>
[Collection("IntegrationTests")]
public sealed class TenantIsolationTests
{
    private readonly DatabaseFixture _fixture;

    public TenantIsolationTests(DatabaseFixture fixture) => _fixture = fixture;

    [RequiresDockerFact]
    public async Task WabaPhoneNumbers_TenantBQueriesUnderRealRls_NeverSeesTenantARow()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var businessAccountA = Guid.NewGuid();
        var phoneNumberA = Guid.NewGuid();

        await SqlSeeding.SeedTenantAsync(_fixture.AdminConnectionString, tenantA, $"it-a-{tenantA:N}"[..18]);
        await SqlSeeding.SeedTenantAsync(_fixture.AdminConnectionString, tenantB, $"it-b-{tenantB:N}"[..18]);
        await SqlSeeding.SeedBusinessAccountAsync(_fixture.AdminConnectionString, businessAccountA, tenantA, $"waba-{businessAccountA:N}"[..18]);
        await SqlSeeding.SeedPhoneNumberAsync(_fixture.AdminConnectionString, phoneNumberA, tenantA, businessAccountA, $"meta-{phoneNumberA:N}"[..15]);

        var (provider, currentTenant) = TestHost.BuildGatewayProviderWithTestTenant(_fixture.AppConnectionString);
        await using var disposableProvider = provider;

        // Tenant A, via the app_user connection (RLS genuinely enforced, not bypassed by a
        // superuser), sees its own row.
        currentTenant.TenantId = tenantA;
        await using (var scopeA = provider.CreateAsyncScope())
        {
            var dbA = scopeA.ServiceProvider.GetRequiredService<IWaGatewayDbContext>();
            var seenByA = await dbA.WabaPhoneNumbers.AsNoTracking().ToListAsync();
            var onlyRow = Assert.Single(seenByA);
            Assert.Equal(phoneNumberA, onlyRow.Id);
        }

        // Same table, same connection role, DIFFERENT tenant GUC — must see nothing at all.
        currentTenant.TenantId = tenantB;
        await using (var scopeB = provider.CreateAsyncScope())
        {
            var dbB = scopeB.ServiceProvider.GetRequiredService<IWaGatewayDbContext>();
            var seenByB = await dbB.WabaPhoneNumbers.AsNoTracking().ToListAsync();
            Assert.Empty(seenByB);

            // Confirms this is RLS itself, not an accidental app-level filter that happens never
            // to run: a direct point lookup by id, with no tenant predicate anywhere in the LINQ,
            // still returns nothing — the database, not the query shape, is the one hiding the row.
            var directLookup = await dbB.WabaPhoneNumbers.AsNoTracking().FirstOrDefaultAsync(p => p.Id == phoneNumberA);
            Assert.Null(directLookup);
        }
    }
}
