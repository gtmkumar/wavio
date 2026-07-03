using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace wavio.Utilities.Endpoints;

public static class WebApplicationExtensions
{
    /// <summary>
    /// Discovers every <see cref="IEndpointGroup"/> in <paramref name="assembly"/> and registers it
    /// as a route group (prefix <c>/api/{ClassName}</c> unless <see cref="IEndpointGroup.RoutePrefix"/>
    /// is overridden) with a matching OpenAPI tag.
    /// </summary>
    public static WebApplication MapEndpoints(this WebApplication app, Assembly assembly)
    {
        var groups = assembly.GetExportedTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false } && t.IsAssignableTo(typeof(IEndpointGroup)));

        foreach (var type in groups)
        {
            var name = type.Name;
            var prefix = type.GetProperty(nameof(IEndpointGroup.RoutePrefix))?.GetValue(null) as string ?? $"/api/{name}";
            var group = app.MapGroup(prefix).WithTags(name);
            type.GetMethod(nameof(IEndpointGroup.Map))!.Invoke(null, [group]);
        }

        return app;
    }
}
