using FluentValidation;
using ValidationException = wavio.Utilities.Exceptions.ValidationException;

namespace wavio.Utilities.Validation;

/// <summary>
/// Minimal-API endpoint filter that runs the registered FluentValidation validators for the bound
/// argument of type <typeparamref name="T"/> (the request/command body) before the handler runs.
/// On failure it throws <see cref="ValidationException"/>, which the global exception middleware
/// maps to a 422 with the standard response envelope. Attach with
/// <c>.AddEndpointFilter&lt;ValidationFilter&lt;T&gt;&gt;()</c>.
/// </summary>
public sealed class ValidationFilter<T> : IEndpointFilter
    where T : notnull
{
    private readonly IEnumerable<IValidator<T>> _validators;

    public ValidationFilter(IEnumerable<IValidator<T>> validators) => _validators = validators;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        // Fail fast on misconfiguration: if the filter is attached for a type the endpoint doesn't
        // bind, silently skipping would bypass validation unnoticed (the exact bug this guards).
        var target = context.Arguments.OfType<T>().FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"ValidationFilter<{typeof(T).Name}> is attached to an endpoint that binds no {typeof(T).Name} argument.");

        if (_validators.Any())
        {
            var validationContext = new ValidationContext<T>(target);

            var results = await Task.WhenAll(
                _validators.Select(v => v.ValidateAsync(validationContext, context.HttpContext.RequestAborted)));

            var failures = results
                .SelectMany(r => r.Errors)
                .Where(f => f is not null)
                .ToList();

            if (failures.Count > 0)
            {
                var errors = failures
                    .GroupBy(f => f.PropertyName, f => f.ErrorMessage)
                    .ToDictionary(g => g.Key, g => g.ToArray());

                throw new ValidationException(errors);
            }
        }

        return await next(context);
    }
}
