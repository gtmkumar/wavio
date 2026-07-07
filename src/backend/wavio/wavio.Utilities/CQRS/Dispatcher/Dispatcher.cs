using Wavio.Utilities.CQRS.Abstractions;

namespace Wavio.Utilities.CQRS.Dispatcher;

/// <summary>
/// The registered <see cref="IDispatcher"/> — delegates to <see cref="CommandDispatcher"/> /
/// <see cref="QueryDispatcher"/> so every dispatch runs through the registered
/// <see cref="IPipelineBehavior{TRequest,TResult}"/> chain. Services that register no behaviors
/// get a plain handler call, unchanged.
/// </summary>
public sealed class Dispatcher : IDispatcher
{
    private readonly CommandDispatcher _commands;
    private readonly QueryDispatcher _queries;

    public Dispatcher(IServiceProvider serviceProvider)
    {
        _commands = new CommandDispatcher(serviceProvider);
        _queries = new QueryDispatcher(serviceProvider);
    }

    public Task<TResult> SendAsync<TResult>(
        ICommand<TResult> command,
        CancellationToken cancellationToken = default) =>
        _commands.SendAsync(command, cancellationToken);

    public Task<TResult> QueryAsync<TResult>(
        IQuery<TResult> query,
        CancellationToken cancellationToken = default) =>
        _queries.QueryAsync(query, cancellationToken);
}
