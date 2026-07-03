using Microsoft.AspNetCore.Routing;

namespace wavio.Utilities.Endpoints;

/// <summary>
/// Defines a group of related Minimal API endpoints. Implementations are discovered and
/// registered by <see cref="WebApplicationExtensions.MapEndpoints"/> as a route group with a
/// matching OpenAPI tag. Default prefix is <c>/api/{ClassName}</c>; override <see cref="RoutePrefix"/>.
/// </summary>
public interface IEndpointGroup
{
    static virtual string? RoutePrefix => null;

    static abstract void Map(RouteGroupBuilder groupBuilder);
}
