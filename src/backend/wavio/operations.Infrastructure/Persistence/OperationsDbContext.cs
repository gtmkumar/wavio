using wavio.SharedDataModel.Entities.Example;
using wavio.SharedDataModel.Persistence;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;

namespace operations.Infrastructure.Persistence;

/// <summary>
/// Adapts the shared <see cref="WavioDbContext"/> to <see cref="IOperationsDbContext"/>, exposing
/// only the entity sets the operations slices use. Lets Application handlers depend on the context
/// surface they own without taking a dependency on the shared concrete context.
/// Mirrors <c>CoreDbContext</c>. Add a DbSet per-slice as features are built.
/// </summary>
public sealed class OperationsDbContext : IOperationsDbContext
{
    private readonly WavioDbContext _db;

    public OperationsDbContext(WavioDbContext db) => _db = db;

    public DbSet<Widget> Widgets => _db.Widgets;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        _db.SaveChangesAsync(cancellationToken);

    /// <inheritdoc/>
    public Task<int> ExecuteSqlInterpolatedAsync(FormattableString sql, CancellationToken cancellationToken) =>
        _db.Database.ExecuteSqlInterpolatedAsync(sql, cancellationToken);

    /// <inheritdoc/>
    public Task<T> SqlQueryScalarAsync<T>(FormattableString sql, CancellationToken cancellationToken) =>
        _db.Database.SqlQuery<T>(sql).SingleAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        // The retrying execution strategy owns the transaction boundary — opening one outside it
        // throws. See IOperationsDbContext.ExecuteInTransactionAsync remarks.
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
            await action(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        });
    }
}
