using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace wavio.Utilities.Endpoints;

/// <summary>
/// Convenience overloads used inside <see cref="IEndpointGroup.Map"/>. Each wraps the standard
/// <c>Map{Verb}</c> and derives the endpoint name from the handler method (→ OpenAPI operationId).
/// </summary>
public static class EndpointRouteBuilderExtensions
{
    public static RouteHandlerBuilder MapGet(this IEndpointRouteBuilder builder, Delegate handler, [StringSyntax("Route")] string pattern = "")
        => builder.MapGet(pattern, handler).WithName(EndpointName(handler));

    public static RouteHandlerBuilder MapPost(this IEndpointRouteBuilder builder, Delegate handler, [StringSyntax("Route")] string pattern = "")
        => builder.MapPost(pattern, handler).WithName(EndpointName(handler));

    public static RouteHandlerBuilder MapPut(this IEndpointRouteBuilder builder, Delegate handler, [StringSyntax("Route")] string pattern)
        => builder.MapPut(pattern, handler).WithName(EndpointName(handler));

    public static RouteHandlerBuilder MapPatch(this IEndpointRouteBuilder builder, Delegate handler, [StringSyntax("Route")] string pattern)
        => builder.MapPatch(pattern, handler).WithName(EndpointName(handler));

    public static RouteHandlerBuilder MapDelete(this IEndpointRouteBuilder builder, Delegate handler, [StringSyntax("Route")] string pattern)
        => builder.MapDelete(pattern, handler).WithName(EndpointName(handler));

    /// <summary>Endpoint names must be globally unique, but handler method names (GetAll, Create, …)
    /// repeat across endpoint groups. Qualify with the declaring type (the endpoint class) so each
    /// name — and the derived OpenAPI operationId — is unique, e.g. <c>AppBanners_GetAll</c>.</summary>
    private static string EndpointName(Delegate handler)
    {
        var method = handler.Method;
        var declaringType = method.DeclaringType?.Name;
        return declaringType is null ? method.Name : $"{declaringType}_{method.Name}";
    }
}
