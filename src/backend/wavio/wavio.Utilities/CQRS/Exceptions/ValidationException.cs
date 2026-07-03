using FluentValidation.Results;

namespace Wavio.Utilities.CQRS.Exceptions;

/// <summary>
/// Raised by the <c>ValidationBehavior</c> when one or more FluentValidation rules fail
/// for a request before its handler runs. Carries the failures grouped by property name.
/// </summary>
public sealed class ValidationException : CqrsException
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public ValidationException()
        : base("One or more validation failures occurred.")
    {
        Errors = new Dictionary<string, string[]>();
    }

    public ValidationException(IEnumerable<ValidationFailure> failures)
        : this()
    {
        Errors = failures
            .GroupBy(f => f.PropertyName, f => f.ErrorMessage)
            .ToDictionary(g => g.Key, g => g.ToArray());
    }
}
