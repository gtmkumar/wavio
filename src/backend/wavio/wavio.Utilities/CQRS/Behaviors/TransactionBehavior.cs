using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Wavio.Utilities.CQRS.Behaviors;

/// <summary>
/// Wraps any request marked with <see cref="IUnitOfWorkCommand"/> in a single EF Core transaction,
/// committing on success and rolling back on failure. Requests without the marker run untouched.
/// </summary>
/// <remarks>
/// Resolves the ambient <see cref="DbContext"/> from DI. Because <c>wavio.Utilities</c> does not
/// reference any concrete context, the application must register its <c>DbContext</c> as the base
/// <see cref="DbContext"/> type (e.g. <c>services.AddScoped&lt;DbContext&gt;(sp =&gt; sp.GetRequiredService&lt;WavioDbContext&gt;())</c>)
/// for this behavior to participate; otherwise it transparently no-ops.
/// </remarks>
public sealed class TransactionBehavior<TRequest, TResult>
    : IPipelineBehavior<TRequest, TResult>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TransactionBehavior<TRequest, TResult>> _logger;

    public TransactionBehavior(
        IServiceProvider serviceProvider,
        ILogger<TransactionBehavior<TRequest, TResult>> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<TResult> HandleAsync(
        TRequest request,
        CancellationToken cancellationToken,
        Func<Task<TResult>> next)
    {
        if (request is not IUnitOfWorkCommand)
        {
            return await next();
        }

        var dbContext = _serviceProvider.GetService<DbContext>();
        if (dbContext is null)
        {
            // No ambient context registered: nothing to coordinate, run the handler directly.
            return await next();
        }

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var result = await next();

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return result;
        }
        catch
        {
            _logger.LogWarning(
                "Rolling back transaction for {Request}",
                typeof(TRequest).Name);

            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
