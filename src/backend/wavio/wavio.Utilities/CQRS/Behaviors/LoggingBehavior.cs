using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.Extensions.Logging;

namespace Wavio.Utilities.CQRS.Behaviors;

public sealed class LoggingBehavior<TRequest, TResult>
    : IPipelineBehavior<TRequest, TResult>
{
    private readonly ILogger<LoggingBehavior<TRequest, TResult>> _logger;

    public LoggingBehavior(
        ILogger<LoggingBehavior<TRequest, TResult>> logger)
    {
        _logger = logger;
    }

    public async Task<TResult> HandleAsync(
        TRequest request,
        CancellationToken cancellationToken,
        Func<Task<TResult>> next)
    {
        _logger.LogInformation(
            "Executing {Request}",
            typeof(TRequest).Name);

        var result = await next();

        _logger.LogInformation(
            "Completed {Request}",
            typeof(TRequest).Name);

        return result;
    }
}
