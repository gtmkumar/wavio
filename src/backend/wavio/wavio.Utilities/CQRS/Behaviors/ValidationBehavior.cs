using FluentValidation;
using Wavio.Utilities.CQRS.Abstractions;
using ValidationException = Wavio.Utilities.CQRS.Exceptions.ValidationException;

namespace Wavio.Utilities.CQRS.Behaviors;

public sealed class ValidationBehavior<TRequest, TResult>
    : IPipelineBehavior<TRequest, TResult>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(
        IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResult> HandleAsync(
        TRequest request,
        CancellationToken cancellationToken,
        Func<Task<TResult>> next)
    {
        if (_validators.Any())
        {
            var context =
                new ValidationContext<TRequest>(request);

            var failures = _validators
                .Select(v => v.Validate(context))
                .SelectMany(v => v.Errors)
                .Where(x => x != null)
                .ToList();

            if (failures.Any())
                throw new ValidationException(failures);
        }

        return await next();
    }
}
