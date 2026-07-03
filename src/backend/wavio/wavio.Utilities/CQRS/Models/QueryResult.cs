namespace Wavio.Utilities.CQRS.Models;

/// <summary>
/// Result of a query carrying the read payload of type <typeparamref name="TData"/>.
/// </summary>
public sealed class QueryResult<TData> : ExecutionResult
{
    private QueryResult(bool succeeded, TData? data, IReadOnlyList<string> errors)
        : base(succeeded, errors)
    {
        Data = data;
    }

    public TData? Data { get; }

    public static QueryResult<TData> Success(TData data) =>
        new(true, data, Array.Empty<string>());

    public static QueryResult<TData> Failure(params string[] errors) =>
        new(false, default, errors);
}
