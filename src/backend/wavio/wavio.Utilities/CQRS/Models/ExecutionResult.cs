namespace Wavio.Utilities.CQRS.Models;

/// <summary>
/// Outcome envelope shared by command and query results: a success flag plus any error messages.
/// </summary>
public abstract class ExecutionResult
{
    protected ExecutionResult(bool succeeded, IReadOnlyList<string> errors)
    {
        Succeeded = succeeded;
        Errors = errors;
    }

    public bool Succeeded { get; }

    public bool Failed => !Succeeded;

    public IReadOnlyList<string> Errors { get; }
}
