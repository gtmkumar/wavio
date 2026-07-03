using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace wavio.Utilities.OpenApi;

/// <summary>
/// Adds the Bearer JWT security scheme to the OpenAPI document when Bearer authentication is
/// configured, so an interactive UI (Scalar/Swagger) shows an "Authorize" button and sends
/// <c>Authorization: Bearer &lt;token&gt;</c>. No-ops when no "Bearer" scheme is registered.
/// </summary>
internal sealed class BearerSecuritySchemeTransformer(IAuthenticationSchemeProvider authenticationSchemeProvider)
    : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(
        OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var schemes = await authenticationSchemeProvider.GetAllSchemesAsync();
        if (!schemes.Any(s => s.Name == "Bearer"))
            return;

        var requirements = new Dictionary<string, IOpenApiSecurityScheme>
        {
            ["Bearer"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                In = ParameterLocation.Header,
                BearerFormat = "Json Web Token"
            }
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes = requirements;
    }
}
