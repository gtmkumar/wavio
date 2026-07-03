using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace wavio.Utilities.OpenApi;

/// <summary>
/// Documents the standard error responses every operation can return through the global
/// <c>ExceptionHandler</c> middleware. Write operations get 422 (FluentValidation failures surface
/// via <c>ValidationFilter</c> → <c>ValidationException</c>); operations behind authorization get
/// 401/403. All use the shared <c>Response</c> error envelope at runtime.
/// </summary>
internal sealed class ErrorResponsesOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        operation.Responses ??= [];

        // Mutating operations validate their body via ValidationFilter → 422 on failure.
        if (context.Description.HttpMethod is "POST" or "PUT" or "PATCH")
            operation.Responses.TryAdd("422", new OpenApiResponse { Description = "Validation failed" });

        // Authorized operations can be rejected by the auth/permission layer.
        if (context.Description.ActionDescriptor.EndpointMetadata.Any(m => m is IAuthorizeData))
        {
            operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Unauthorized" });
            operation.Responses.TryAdd("403", new OpenApiResponse { Description = "Forbidden" });
        }

        return Task.CompletedTask;
    }
}
