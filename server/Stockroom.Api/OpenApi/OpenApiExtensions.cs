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

    /// <summary>Serves the document at <c>/api/v1/openapi.json</c> and the interactive reference at <c>/api/docs</c>.</summary>
    public static IEndpointRouteBuilder MapStockroomOpenApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapOpenApi(DocumentRoutePattern);
        endpoints.MapScalarApiReference("/api/docs", options => options
            .WithTitle("Stockroom API")
            .AddDocument(DocumentName)
            .OpenApiRoutePattern = DocumentRoutePattern);
        return endpoints;
    }
}
