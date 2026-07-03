using Wavio.Utilities.CQRS.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Wavio.Utilities.CQRS.Dispatcher;

public sealed class Dispatcher : IDispatcher
{
    private readonly IServiceProvider _serviceProvider;

    public Dispatcher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<TResult> SendAsync<TResult>(
        ICommand<TResult> command,
        CancellationToken cancellationToken = default)
    {
        var handlerType =
            typeof(ICommandHandler<,>)
                .MakeGenericType(command.GetType(), typeof(TResult));

        dynamic handler =
            _serviceProvider.GetRequiredService(handlerType);

        return await handler.HandleAsync(
            (dynamic)command,
            cancellationToken);
    }

    public async Task<TResult> QueryAsync<TResult>(
        IQuery<TResult> query,
        CancellationToken cancellationToken = default)
    {
        var handlerType =
            typeof(IQueryHandler<,>)
                .MakeGenericType(query.GetType(), typeof(TResult));

        dynamic handler =
            _serviceProvider.GetRequiredService(handlerType);

        return await handler.HandleAsync(
            (dynamic)query,
            cancellationToken);
    }
}
