using wavio.SharedDataModel.Entities.Example;
using Microsoft.EntityFrameworkCore;

namespace operations.Application.Common.Interfaces;

/// <summary>
/// The operations context's data-access surface, exposed to Application handlers as an interface
/// (no repositories). Backed by the shared <c>WavioDbContext</c> via an adapter in
/// operations.Infrastructure. Handlers inject this and write EF Core LINQ directly.
/// Only the entity sets the operations slices touch are surfaced here — add a DbSet per
/// entity your feature needs, mirroring ICoreDbContext.
/// </summary>
public interface IOperationsDbContext
{
    // ─── Example vertical slice — delete once real domain entities replace it ─
    DbSet<Widget> Widgets { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Executes a parameterized raw-SQL statement against the underlying connection. Used for
    /// guarded atomic UPDATEs that cannot be expressed as a tracked entity change.
    /// Pass interpolated values — they are bound as parameters, never string-concatenated.
    /// </summary>
    Task<int> ExecuteSqlInterpolatedAsync(FormattableString sql, CancellationToken cancellationToken);

    /// <summary>
    /// Runs a parameterized raw-SQL query that returns a single scalar value and returns the first
    /// row. Used for allocators that delegate to a Postgres function (e.g. an atomic per-tenant
    /// counter) that cannot be expressed as a tracked entity change.
    /// Pass interpolated values — they are bound as parameters, never string-concatenated.
    /// </summary>
    Task<T> SqlQueryScalarAsync<T>(FormattableString sql, CancellationToken cancellationToken);

    /// <summary>
    /// Runs <paramref name="action"/> inside a database transaction, wrapped in the provider's
    /// retrying execution strategy. The strategy owns the transaction boundary — required because
    /// <c>NpgsqlRetryingExecutionStrategy</c> rejects a manually-opened <c>BeginTransactionAsync</c>
    /// unless it is created inside <c>CreateExecutionStrategy().ExecuteAsync(...)</c>. Use only when
    /// the unit of work spans more than a single <see cref="SaveChangesAsync"/> call.
    /// </summary>
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken);
}
