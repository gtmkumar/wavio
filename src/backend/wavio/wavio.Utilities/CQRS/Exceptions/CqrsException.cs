namespace Wavio.Utilities.CQRS.Exceptions;

/// <summary>
/// Base type for every exception raised by the CQRS dispatch pipeline.
/// Catch this to handle any infrastructure-level CQRS failure uniformly.
/// </summary>
public class CqrsException : Exception
{
    public CqrsException(string message)
        : base(message)
    {
    }

    public CqrsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
