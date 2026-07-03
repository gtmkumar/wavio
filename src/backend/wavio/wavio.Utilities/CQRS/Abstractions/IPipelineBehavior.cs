namespace Wavio.Utilities.CQRS.Abstractions;

public interface IPipelineBehavior<TRequest, TResult>
{
    Task<TResult> HandleAsync(
        TRequest request,
        CancellationToken cancellationToken,
        Func<Task<TResult>> next);
}
