using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Helios.Api.Configuration;

/// <summary>
/// The published contract (plan section 8: "Publish OpenAPI … from the actual contract"): document
/// metadata and the two ways to authenticate — a portal session (JWT bearer) or an API key.
/// </summary>
public sealed class HeliosOpenApiDocument : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "AURA HELIOS API",
            Version = "v1",
            Description =
                "South African business APIs behind one integration and one Rand bill. Products are listed with their " +
                "release state at GET /api/v1/catalogue; only products marked callable can be executed, and sandbox " +
                "results are synthetic-data only. See docs/API-QUICKSTART.md for worked examples."
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

        document.Components.SecuritySchemes["portal"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "A signed-in session from POST /api/v1/auth/login (and select-workspace)."
        };

        document.Components.SecuritySchemes["apiKey"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = "X-Api-Key",
            Description = "A scoped key (hk_test_… or hk_live_…). Also accepted as 'Authorization: Bearer hk_…'."
        };

        return Task.CompletedTask;
    }
}
