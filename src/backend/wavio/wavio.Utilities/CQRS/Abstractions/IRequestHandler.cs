namespace Wavio.Utilities.CQRS.Abstractions;

/// <summary>
/// Unifying handler contract for any <see cref="IRequest{TResult}"/>.
/// <see cref="ICommandHandler{TCommand,TResult}"/> and <see cref="IQueryHandler{TQuery,TResult}"/>
/// are the concrete command/query specializations of this shape.
/// </summary>
public interface IRequestHandler<TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    Task<TResult> HandleAsync(
        TRequest request,
        CancellationToken cancellationToken);
}
