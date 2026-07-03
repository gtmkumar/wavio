using Wavio.Utilities.CQRS.Abstractions;
using Wavio.Utilities.CQRS.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace Wavio.Utilities.CQRS.Dispatcher;

/// <summary>
/// Resolves the query handler and wraps its execution in the registered
/// <see cref="IPipelineBehavior{TRequest,TResult}"/> chain (logging, caching, performance, ...).
/// Behaviors run in registration order, outermost first.
/// </summary>
public sealed class QueryDispatcher
{
    private readonly IServiceProvider _serviceProvider;

    public QueryDispatcher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Task<TResult> QueryAsync<TResult>(
        IQuery<TResult> query,
        CancellationToken cancellationToken = default)
    {
        var requestType = query.GetType();

        var handlerType =
            typeof(IQueryHandler<,>)
                .MakeGenericType(requestType, typeof(TResult));

        var handler = _serviceProvider.GetService(handlerType)
            ?? throw new HandlerNotFoundException(requestType);

        Func<Task<TResult>> next = () =>
        {
            dynamic h = handler;
            return (Task<TResult>)h.HandleAsync((dynamic)query, cancellationToken);
        };

        var behaviorType =
            typeof(IPipelineBehavior<,>)
                .MakeGenericType(requestType, typeof(TResult));

        var behaviors = _serviceProvider
            .GetServices(behaviorType)
            .Where(b => b is not null)
            .Reverse()
            .ToList();

        foreach (var behavior in behaviors)
        {
            var current = next;
            dynamic b = behavior!;
            next = () => (Task<TResult>)b.HandleAsync((dynamic)query, cancellationToken, current);
        }

        return next();
    }
}
