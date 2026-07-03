using Wavio.Utilities.CQRS.Abstractions;
using Wavio.Utilities.CQRS.Behaviors;
using Microsoft.Extensions.DependencyInjection;

namespace Wavio.Utilities.CQRS.Registration;

/// <summary>
/// Registers the open-generic <see cref="IPipelineBehavior{TRequest,TResult}"/> implementations.
/// Order matters: behaviors execute outermost-first in the order registered here.
/// </summary>
public static class BehaviorRegistrar
{
    public static IServiceCollection RegisterBehaviors(
        this IServiceCollection services)
    {
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ExceptionBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(AuditBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(CachingBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        return services;
    }
}
