namespace Wavio.Utilities.CQRS.Exceptions;

/// <summary>
/// Thrown when the dispatcher cannot resolve a handler for a given command or query type.
/// Usually indicates a missing handler registration in the DI container.
/// </summary>
public sealed class HandlerNotFoundException : CqrsException
{
    public Type RequestType { get; }

    public HandlerNotFoundException(Type requestType)
        : base($"No handler was registered for request type '{requestType.FullName}'.")
    {
        RequestType = requestType;
    }
}
