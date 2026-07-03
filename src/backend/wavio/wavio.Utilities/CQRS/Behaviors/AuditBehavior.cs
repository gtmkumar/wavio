using Wavio.Utilities.CQRS.Abstractions;
using Wavio.Utilities.CQRS.Context;
using Microsoft.Extensions.Logging;

namespace Wavio.Utilities.CQRS.Behaviors;

/// <summary>
/// Emits an audit trail entry for every command, recording who acted (from <see cref="UserContext"/>)
/// and the correlation id tying the action to its request scope.
/// </summary>
public sealed class AuditBehavior<TRequest, TResult>
    : IPipelineBehavior<TRequest, TResult>
{
    private readonly RequestContext _requestContext;
    private readonly ILogger<AuditBehavior<TRequest, TResult>> _logger;

    public AuditBehavior(
        RequestContext requestContext,
        ILogger<AuditBehavior<TRequest, TResult>> logger)
    {
        _requestContext = requestContext;
        _logger = logger;
    }

    public async Task<TResult> HandleAsync(
        TRequest request,
        CancellationToken cancellationToken,
        Func<Task<TResult>> next)
    {
        var result = await next();

        // Only commands mutate state and warrant an audit record.
        if (request is ICommand<TResult> or IUnitOfWorkCommand)
        {
            _logger.LogInformation(
                "Audit: {User} executed {Request} (correlation {CorrelationId})",
                _requestContext.User.UserId ?? "anonymous",
                typeof(TRequest).Name,
                _requestContext.Correlation.CorrelationId);
        }

        return result;
    }
}
