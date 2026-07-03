using System.Diagnostics;
using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.Extensions.Logging;

namespace Wavio.Utilities.CQRS.Behaviors;

/// <summary>
/// Times each request and logs a warning when it exceeds <see cref="ThresholdMilliseconds"/>,
/// surfacing slow commands/queries for investigation.
/// </summary>
public sealed class PerformanceBehavior<TRequest, TResult>
    : IPipelineBehavior<TRequest, TResult>
{
    private const long ThresholdMilliseconds = 500;

    private readonly ILogger<PerformanceBehavior<TRequest, TResult>> _logger;

    public PerformanceBehavior(
        ILogger<PerformanceBehavior<TRequest, TResult>> logger)
    {
        _logger = logger;
    }

    public async Task<TResult> HandleAsync(
        TRequest request,
        CancellationToken cancellationToken,
        Func<Task<TResult>> next)
    {
        var stopwatch = Stopwatch.StartNew();

        var result = await next();

        stopwatch.Stop();

        if (stopwatch.ElapsedMilliseconds > ThresholdMilliseconds)
        {
            _logger.LogWarning(
                "Long-running request {Request} took {Elapsed} ms",
                typeof(TRequest).Name,
                stopwatch.ElapsedMilliseconds);
        }

        return result;
    }
}
