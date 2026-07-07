using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using wavio.SharedDataModel;
using wavio.SharedDataModel.Contracts;
using wavio.SharedDataModel.Entities.Kernel;
using wavio.SharedDataModel.Persistence;
using Wavio.Utilities.CQRS.Abstractions;
using Wavio.Utilities.CQRS.Behaviors;
using Wavio.Utilities.CQRS.Extensions;
using WaPlatform.IntegrationTests.Support;
using Xunit;

namespace WaPlatform.IntegrationTests.Cqrs;

/// <summary>
/// <see cref="TransactionBehavior{TRequest,TResult}"/> is what makes InviteUser (create user +
/// grant membership, two dispatcher sends each doing its own SaveChanges) atomic — before it was
/// activated, a failed grant left an orphan "No role" user behind (observed live 2026-07-07).
/// The mechanism has three parts that only a REAL Postgres can prove together, which is why this
/// lives here and not in a unit test: (1) the base-<see cref="DbContext"/> alias resolves to the
/// SAME scoped <see cref="WavioDbContext"/> the handlers write through, so the transaction and
/// their SaveChanges share one connection; (2) <c>BeginTransactionAsync</c> must run inside
/// <c>CreateExecutionStrategy()</c> because AddSharedDataModel turns on
/// <c>EnableRetryOnFailure</c> — outside it Npgsql's retrying strategy throws
/// InvalidOperationException on the very first unit-of-work command; (3) an intermediate
/// SaveChanges inside the transaction (InviteUser's exact shape) genuinely rolls back.
/// The command/handler pair below mirrors that shape 1:1 — write, save, maybe fail, write, save —
/// dispatched through the real <see cref="IDispatcher"/> so the whole production path
/// (Dispatcher → CommandDispatcher → behavior chain → handler) is exercised, not the behavior
/// class in isolation.
/// </summary>
[Collection("IntegrationTests")]
public sealed class TransactionBehaviorTests
{
    private readonly DatabaseFixture _fixture;

    public TransactionBehaviorTests(DatabaseFixture fixture) => _fixture = fixture;

    [RequiresDockerFact]
    public async Task SendAsync_UnitOfWorkCommandFailsAfterIntermediateSave_FirstWriteRollsBack()
    {
        var tenantId = Guid.NewGuid();
        var category = $"itest-{Guid.NewGuid():N}"[..30];
        await SqlSeeding.SeedTenantAsync(_fixture.AdminConnectionString, tenantId, $"tb-{tenantId:N}"[..18]);

        await using var provider = BuildProvider(tenantId);
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dispatcher.SendAsync(new WriteTwoSettingsCommand(tenantId, category, FailBetweenWrites: true)));

        // The first write already went through its own SaveChanges before the failure — without
        // the ambient transaction it would be durably committed (the orphan-user bug).
        Assert.Equal(0, await CountSettingsAsync(category));
    }

    [RequiresDockerFact]
    public async Task SendAsync_UnitOfWorkCommandSucceeds_BothWritesCommit()
    {
        var tenantId = Guid.NewGuid();
        var category = $"itest-{Guid.NewGuid():N}"[..30];
        await SqlSeeding.SeedTenantAsync(_fixture.AdminConnectionString, tenantId, $"tb-{tenantId:N}"[..18]);

        await using var provider = BuildProvider(tenantId);
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        var result = await dispatcher.SendAsync(
            new WriteTwoSettingsCommand(tenantId, category, FailBetweenWrites: false));

        Assert.True(result);
        Assert.Equal(2, await CountSettingsAsync(category));
    }

    /// <summary>Same DI shape core.WebApi assembles: AddSharedDataModel (RLS interceptor +
    /// EnableRetryOnFailure), AddCustomCQRS over the handler assembly, TransactionBehavior as the
    /// only pipeline behavior, and the base-DbContext alias (core.Infrastructure's registration).</summary>
    private ServiceProvider BuildProvider(Guid tenantId)
    {
        var services = new ServiceCollection();
        services.AddSharedDataModel(_fixture.AppConnectionString);
        services.AddSingleton<ICurrentTenant>(new TestCurrentTenant { TenantId = tenantId });
        services.AddCustomCQRS(typeof(TransactionBehaviorTests).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<WavioDbContext>());
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        return services.BuildServiceProvider();
    }

    private async Task<long> CountSettingsAsync(string category)
    {
        await using var connection = new NpgsqlConnection(_fixture.AdminConnectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT count(*) FROM kernel.system_settings WHERE category = @category", connection);
        cmd.Parameters.AddWithValue("category", category);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}

// ── Test fixture command — InviteUser's exact write shape ───────────────────
public sealed record WriteTwoSettingsCommand(Guid TenantId, string Category, bool FailBetweenWrites)
    : ICommand<bool>, IUnitOfWorkCommand;

public sealed class WriteTwoSettingsCommandHandler : ICommandHandler<WriteTwoSettingsCommand, bool>
{
    private readonly WavioDbContext _db;

    public WriteTwoSettingsCommandHandler(WavioDbContext db) => _db = db;

    public async Task<bool> HandleAsync(WriteTwoSettingsCommand cmd, CancellationToken ct)
    {
        _db.SystemSettings.Add(NewSetting(cmd, "first"));
        await _db.SaveChangesAsync(ct);

        if (cmd.FailBetweenWrites)
            throw new InvalidOperationException("simulated failure between the two writes");

        _db.SystemSettings.Add(NewSetting(cmd, "second"));
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static SystemSetting NewSetting(WriteTwoSettingsCommand cmd, string key) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = cmd.TenantId,
        ScopeType = "tenant",
        Category = cmd.Category,
        SettingKey = key,
        SettingValue = $"\"{key}\"",
        DataType = "string",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        Version = 1,
        Status = "active",
    };
}
