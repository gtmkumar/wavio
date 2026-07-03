using Wavio.Utilities.CQRS.Abstractions;
using Wavio.Utilities.CQRS.Exceptions;
using Microsoft.Extensions.Logging;

namespace Wavio.Utilities.CQRS.Behaviors;

/// <summary>
/// Logs and normalizes unhandled exceptions thrown by a handler or any inner behavior.
/// CQRS-domain exceptions pass through unchanged; everything else is wrapped in a
/// <see cref="CqrsException"/> so callers see a single failure contract.
/// </summary>
public sealed class ExceptionBehavior<TRequest, TResult>
    : IPipelineBehavior<TRequest, TResult>
{
    private readonly ILogger<ExceptionBehavior<TRequest, TResult>> _logger;

    public ExceptionBehavior(
        ILogger<ExceptionBehavior<TRequest, TResult>> logger)
    {
        _logger = logger;
    }

    public async Task<TResult> HandleAsync(
        TRequest request,
        CancellationToken cancellationToken,
        Func<Task<TResult>> next)
    {
        try
        {
            return await next();
        }
        catch (CqrsException)
        {
            // Already a CQRS-shaped failure (validation, handler-not-found, ...): let it bubble.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unhandled exception while processing {Request}",
                typeof(TRequest).Name);

            throw new CqrsException(
                $"An error occurred while processing '{typeof(TRequest).Name}'.",
                ex);
        }
    }
}
