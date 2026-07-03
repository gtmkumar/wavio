using Microsoft.AspNetCore.Builder;
using Scalar.AspNetCore;

namespace wavio.Utilities.OpenApi;

/// <summary>
/// Maps the OpenAPI document and the Scalar API reference UI. Pairs with
/// <see cref="OpenApiServiceCollectionExtensions.AddDefaultOpenApi"/>. Call inside the host's
/// Development guard — the document/UI should not be exposed in production.
/// </summary>
public static class OpenApiWebApplicationExtensions
{
    public static WebApplication MapDefaultOpenApi(this WebApplication app)
    {
        app.MapOpenApi();              // /openapi/v1.json
        app.MapScalarApiReference();   // /scalar (interactive UI)
        return app;
    }
}
