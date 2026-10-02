using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace DYS.Molargo.Api.Infrastructure;

/// <summary>
/// Declares the bearer scheme in the OpenAPI document.
/// </summary>
/// <remarks>
/// Without it the document describes an API where every route answers 401 and says nothing
/// about how to get a token — and the interactive client has no field to paste one into,
/// which makes the whole document read-only.
/// </remarks>
public sealed class BearerSchemeTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();

        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description =
                "A token from POST /auth/token. Everything except that route needs one.",
        };

        return Task.CompletedTask;
    }
}
