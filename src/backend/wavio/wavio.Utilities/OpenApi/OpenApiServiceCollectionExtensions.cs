using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;

namespace wavio.Utilities.OpenApi;

/// <summary>
/// Registers the OpenAPI document with the project's default transformers (Bearer security scheme +
/// standard error responses). Pair with <c>app.MapOpenApi()</c> to serve <c>/openapi/v1.json</c>.
/// </summary>
public static class OpenApiServiceCollectionExtensions
{
    public static IServiceCollection AddDefaultOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
            options.AddOperationTransformer<ErrorResponsesOperationTransformer>();
        });

        return services;
    }
}
