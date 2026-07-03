namespace Wavio.Utilities.CQRS.Abstractions;

/// <summary>
/// Marker for any dispatchable request (command or query) that yields <typeparamref name="TResult"/>.
/// Behaviors and the dispatcher treat commands and queries uniformly through this contract.
/// </summary>
public interface IRequest<TResult>
{
}
