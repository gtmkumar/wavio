namespace Wavio.Utilities.CQRS.Models;

/// <summary>
/// Result of a command. Use the static factories to express success or failure consistently.
/// </summary>
public sealed class CommandResult : ExecutionResult
{
    private CommandResult(bool succeeded, IReadOnlyList<string> errors)
        : base(succeeded, errors)
    {
    }

    public static CommandResult Success() =>
        new(true, Array.Empty<string>());

    public static CommandResult Failure(params string[] errors) =>
        new(false, errors);
}

/// <summary>
/// Result of a command that produces a value of type <typeparamref name="TValue"/>.
/// </summary>
public sealed class CommandResult<TValue> : ExecutionResult
{
    private CommandResult(bool succeeded, TValue? value, IReadOnlyList<string> errors)
        : base(succeeded, errors)
    {
        Value = value;
    }

    public TValue? Value { get; }

    public static CommandResult<TValue> Success(TValue value) =>
        new(true, value, Array.Empty<string>());

    public static CommandResult<TValue> Failure(params string[] errors) =>
        new(false, default, errors);
}
