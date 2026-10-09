using Scalar.AspNetCore;

namespace Stockroom.Api.OpenApi;

internal static class OpenApiExtensions
{
    /// <summary>Document name; it is also the URL segment, so the document is served at <c>/api/v1/openapi.json</c>.</summary>
    public const string DocumentName = "v1";

    private const string DocumentRoutePattern = "/api/{documentName}/openapi.json";

    /// <summary>Generates the OpenAPI 3.1 document from the mapped endpoints (spec 9.1, 10).</summary>
    public static IServiceCollection AddStockroomOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(DocumentName, options => options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info.Title = "Stockroom API";
            document.Info.Version = DocumentName;
            document.Info.Description = "Self-hosted inventory and stock tracking.";
            return Task.CompletedTask;
        }));

    /// <summary>
    /// Serves the document at <c>/api/v1/openapi.json</c> in every environment, since clients are generated
    /// from it. The interactive reference at <c>/api/docs</c> is a development tool and is only mapped in
    /// the Development environment.
    /// </summary>
    public static IEndpointRouteBuilder MapStockroomOpenApi(this IEndpointRouteBuilder endpoints, IHostEnvironment environment)
    {
        // Public: clients are generated from it, and it reveals nothing the endpoints themselves do not.
        endpoints.MapOpenApi(DocumentRoutePattern).AllowAnonymous();

        if (environment.IsDevelopment())
        {
            endpoints.MapScalarApiReference("/api/docs", options => options
                .WithTitle("Stockroom API")
                .AddDocument(DocumentName)
                .OpenApiRoutePattern = DocumentRoutePattern)
                .AllowAnonymous();
        }

        return endpoints;
    }
}
