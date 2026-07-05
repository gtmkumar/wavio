using WaAdmin.Application.Consent.Dtos;
using WaAdmin.Application.RetentionPolicies.Commands.UpsertRetentionPolicy;
using WaAdmin.Application.RetentionPolicies.Queries.GetRetentionPolicies;
using WaAdmin.Tests.Fakes;
using wavio.SharedDataModel.Entities.Consent;
using wavio.Utilities.Exceptions;
using Xunit;

namespace WaAdmin.Tests.RetentionPolicies;

public class RetentionPolicyHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static InMemoryWaAdminDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string name = "") =>
        InMemoryWaAdminDbContext.Create(name);

    private static void SeedPlatformDefault(InMemoryWaAdminDbContext db, string dataClass, int days)
    {
        var now = DateTimeOffset.UtcNow;
        db.RetentionPolicies.Add(new RetentionPolicy
        {
            Id = Guid.NewGuid(), TenantId = null, DataClass = dataClass, RetentionDays = days,
            Basis = "platform_default", Enabled = true, CreatedAt = now, UpdatedAt = now, Version = 1,
        });
        db.SaveChangesAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task GetRetentionPolicies_NoTenantOverride_ReturnsPlatformDefault()
    {
        await using var db = NewDb();
        SeedPlatformDefault(db, "message_content", 365);
        var handler = new GetRetentionPoliciesQueryHandler(db);

        var result = await handler.HandleAsync(new GetRetentionPoliciesQuery(TenantId), CancellationToken.None);

        var messageContent = result.Single(p => p.DataClass == "message_content");
        Assert.Null(messageContent.TenantId);
        Assert.Equal(365, messageContent.RetentionDays);
    }

    [Fact]
    public async Task GetRetentionPolicies_TenantOverrideExists_PrefersOverrideOverPlatformDefault()
    {
        await using var db = NewDb();
        SeedPlatformDefault(db, "message_content", 365);
        var upsertHandler = new UpsertRetentionPolicyCommandHandler(db);
        await upsertHandler.HandleAsync(
            new UpsertRetentionPolicyCommand(
                new UpsertRetentionPolicyRequest("message_content", 180, "tenant_prefers_shorter", true),
                TenantId, Guid.NewGuid()),
            CancellationToken.None);

        var getHandler = new GetRetentionPoliciesQueryHandler(db);
        var result = await getHandler.HandleAsync(new GetRetentionPoliciesQuery(TenantId), CancellationToken.None);

        var messageContent = result.Single(p => p.DataClass == "message_content");
        Assert.Equal(TenantId, messageContent.TenantId);
        Assert.Equal(180, messageContent.RetentionDays);
    }

    [Fact]
    public async Task UpsertRetentionPolicy_CalledTwice_UpdatesExistingOverrideRatherThanDuplicating()
    {
        await using var db = NewDb();
        var handler = new UpsertRetentionPolicyCommandHandler(db);
        var request = new UpsertRetentionPolicyRequest("metadata", 2920, "tax_retention_8y", true);

        await handler.HandleAsync(new UpsertRetentionPolicyCommand(request, TenantId, Guid.NewGuid()), CancellationToken.None);
        var second = await handler.HandleAsync(
            new UpsertRetentionPolicyCommand(request with { RetentionDays = 3000 }, TenantId, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(3000, second.RetentionDays);
        Assert.Single(db.RetentionPolicies.Where(p => p.TenantId == TenantId && p.DataClass == "metadata"));
    }

    [Fact]
    public async Task UpsertRetentionPolicy_NeverWritesToThePlatformDefaultRow()
    {
        await using var db = NewDb();
        SeedPlatformDefault(db, "raw_webhook", 30);
        var handler = new UpsertRetentionPolicyCommandHandler(db);

        await handler.HandleAsync(
            new UpsertRetentionPolicyCommand(
                new UpsertRetentionPolicyRequest("raw_webhook", 60, "tenant_override", true), TenantId, Guid.NewGuid()),
            CancellationToken.None);

        var platformDefault = db.RetentionPolicies.Single(p => p.TenantId == null && p.DataClass == "raw_webhook");
        Assert.Equal(30, platformDefault.RetentionDays); // untouched
        Assert.Equal(2, db.RetentionPolicies.Count(p => p.DataClass == "raw_webhook")); // default + override, both present
    }

    [Theory]
    [InlineData("not_a_real_data_class", 30)]
    [InlineData("message_content", 0)]
    [InlineData("message_content", -5)]
    public async Task UpsertRetentionPolicy_InvalidRequest_ThrowsValidationException(string dataClass, int retentionDays)
    {
        await using var db = NewDb();
        var handler = new UpsertRetentionPolicyCommandHandler(db);

        await Assert.ThrowsAsync<ValidationException>(() => handler.HandleAsync(
            new UpsertRetentionPolicyCommand(new UpsertRetentionPolicyRequest(dataClass, retentionDays, null, true), TenantId, null),
            CancellationToken.None));
    }
}
