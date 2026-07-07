using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WaAdmin.Application.Common.Interfaces;
using WaAdmin.Application.Onboarding.Commands.CompleteEmbeddedSignup;
using WaAdmin.Application.Onboarding.Dtos;
using WaAdmin.Infrastructure.Persistence;
using wavio.SharedDataModel;
using wavio.SharedDataModel.Contracts;
using wavio.SharedDataModel.Crypto;
using WaPlatform.IntegrationTests.Support;
using Xunit;

namespace WaPlatform.IntegrationTests.Onboarding;

/// <summary>
/// The embedded-signup upsert against a REAL Postgres with RLS enforced (app_user connection):
/// (1) the whole WABA + phone persistence lands under the caller's tenant GUC and the token is
/// stored as ciphertext; (2) re-running the signup upserts by meta ids instead of tripping the
/// global unique constraints; (3) another tenant's session cannot see the row — the property the
/// EF InMemory provider cannot prove, hence this project (docs/ONBOARDING_WIZARD_PLAN.md Phase 4).
/// </summary>
[Collection("IntegrationTests")]
public sealed class EmbeddedSignupIntegrationTests
{
    private readonly DatabaseFixture _fixture;

    public EmbeddedSignupIntegrationTests(DatabaseFixture fixture) => _fixture = fixture;

    [RequiresDockerFact]
    public async Task HandleAsync_SignupThenRerun_UpsertsEncryptedRowVisibleOnlyToOwnTenant()
    {
        var tenantId = Guid.NewGuid();
        await SqlSeeding.SeedTenantAsync(_fixture.AdminConnectionString, tenantId, $"ob-{tenantId:N}"[..18]);

        // Meta ids unique per run so repeated local runs against a warm container never collide.
        var suffix = $"{Guid.NewGuid():N}"[..12];
        var wabaId = $"10{suffix}";
        var phoneId = $"20{suffix}";
        var graph = new FakeOnboardingGraphClient(wabaId, phoneId);
        var command = new CompleteEmbeddedSignupCommand(
            new EmbeddedSignupRequest($"SIMCODE-{tenantId}", null, null), tenantId, null);

        await using var provider = BuildProvider(tenantId);
        await using (var scope = provider.CreateAsyncScope())
        {
            var handler = new CompleteEmbeddedSignupCommandHandler(
                scope.ServiceProvider.GetRequiredService<IWaAdminDbContext>(), graph, new PassThroughCipher());
            var status = await handler.HandleAsync(command, CancellationToken.None);
            Assert.True(status.Connected);
        }

        // Re-run in a fresh scope — same WABA must upsert, not duplicate or violate uniques.
        await using (var scope = provider.CreateAsyncScope())
        {
            var handler = new CompleteEmbeddedSignupCommandHandler(
                scope.ServiceProvider.GetRequiredService<IWaAdminDbContext>(), graph, new PassThroughCipher());
            await handler.HandleAsync(command, CancellationToken.None);
        }

        // Admin (RLS-bypassing) truth: exactly one row, tenant-stamped, ciphertext not plaintext.
        await using var connection = new NpgsqlConnection(_fixture.AdminConnectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT count(*), min(tenant_id::text), min(system_user_token_ciphertext)
            FROM waba.business_accounts WHERE meta_waba_id = @wabaId
            """, connection);
        cmd.Parameters.AddWithValue("wabaId", wabaId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt64(0));
        Assert.Equal(tenantId.ToString(), reader.GetString(1));
        Assert.StartsWith("cipher:", reader.GetString(2), StringComparison.Ordinal);
        await reader.CloseAsync();

        // RLS: a session running as a DIFFERENT tenant must not see the row via app_user.
        var otherTenantId = Guid.NewGuid();
        await SqlSeeding.SeedTenantAsync(_fixture.AdminConnectionString, otherTenantId, $"ob-{otherTenantId:N}"[..18]);
        await using var otherProvider = BuildProvider(otherTenantId);
        await using var otherScope = otherProvider.CreateAsyncScope();
        var otherDb = otherScope.ServiceProvider.GetRequiredService<IWaAdminDbContext>();
        var visible = await otherDb.WabaBusinessAccounts.AsNoTracking()
            .CountAsync(a => a.MetaWabaId == wabaId, CancellationToken.None);
        Assert.Equal(0, visible);
    }

    private ServiceProvider BuildProvider(Guid tenantId)
    {
        var services = new ServiceCollection();
        services.AddSharedDataModel(_fixture.AppConnectionString);
        services.AddSingleton<ICurrentTenant>(new TestCurrentTenant { TenantId = tenantId });
        services.AddScoped<IWaAdminDbContext, WaAdminDbContext>();
        return services.BuildServiceProvider();
    }

    /// <summary>Hand-rolled happy-path Graph fake — one WABA carrying one PENDING phone.</summary>
    private sealed class FakeOnboardingGraphClient(string wabaId, string phoneId) : IWhatsAppOnboardingGraphClient
    {
        public Task<GraphTokenResult> ExchangeCodeAsync(string code, CancellationToken cancellationToken) =>
            Task.FromResult(new GraphTokenResult(true, $"business-token-{wabaId}", null));

        public Task<IReadOnlyList<string>> GetGrantedWabaIdsAsync(string accessToken, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>([wabaId]);

        public Task<GraphWabaInfo?> GetBusinessAccountAsync(string accessToken, string metaWabaId, CancellationToken cancellationToken) =>
            Task.FromResult<GraphWabaInfo?>(new GraphWabaInfo(wabaId, "Integration Demo", "INR", null, "pending"));

        public Task<IReadOnlyList<GraphPhoneInfo>> GetPhoneNumbersAsync(string accessToken, string metaWabaId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GraphPhoneInfo>>(
                [new GraphPhoneInfo(phoneId, "+91 90000 00042", "Integration Demo", "PENDING",
                    "NOT_VERIFIED", "NONE", "GREEN", "TIER_1K")]);

        public Task<GraphPhoneInfo?> GetPhoneNumberAsync(string accessToken, string metaPhoneNumberId, CancellationToken cancellationToken) =>
            Task.FromResult<GraphPhoneInfo?>(null);

        public Task<GraphOpResult> SubscribeAppAsync(string accessToken, string metaWabaId, CancellationToken cancellationToken) =>
            Task.FromResult(GraphOpResult.Ok);

        public Task<GraphOpResult> RegisterPhoneAsync(string accessToken, string metaPhoneNumberId, string pin, CancellationToken cancellationToken) =>
            Task.FromResult(GraphOpResult.Ok);

        public Task<GraphOpResult> RequestVerificationCodeAsync(string accessToken, string metaPhoneNumberId, string codeMethod, string language, CancellationToken cancellationToken) =>
            Task.FromResult(GraphOpResult.Ok);

        public Task<GraphOpResult> VerifyCodeAsync(string accessToken, string metaPhoneNumberId, string code, CancellationToken cancellationToken) =>
            Task.FromResult(GraphOpResult.Ok);

        public Task<GraphBusinessProfile?> GetBusinessProfileAsync(string accessToken, string metaPhoneNumberId, CancellationToken cancellationToken) =>
            Task.FromResult<GraphBusinessProfile?>(null);

        public Task<GraphOpResult> UpdateBusinessProfileAsync(string accessToken, string metaPhoneNumberId, GraphBusinessProfile profile, CancellationToken cancellationToken) =>
            Task.FromResult(GraphOpResult.Ok);
    }

    /// <summary>Marks values so the ciphertext assertion can tell "went through the cipher"
    /// apart from "stored raw" — the cryptography itself is AesGcmFieldCipher's own concern.</summary>
    private sealed class PassThroughCipher : IFieldCipher
    {
        public string? Encrypt(string? plaintext) => plaintext is null ? null : $"cipher:{plaintext}";
        public string? Decrypt(string? value) =>
            value is null ? null : value.StartsWith("cipher:", StringComparison.Ordinal) ? value["cipher:".Length..] : value;
    }
}
