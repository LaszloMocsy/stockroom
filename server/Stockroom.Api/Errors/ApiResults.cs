using Microsoft.AspNetCore.Http.HttpResults;

namespace Stockroom.Api.Errors;

/// <summary>Results for endpoints that fail with a specific error code.</summary>
public static class ApiResults
{
    internal const string CodeKey = "stockroom.code";
    internal const string DetailsKey = "stockroom.details";

    /// <summary>
    /// An error response with the given status and code, e.g.
    /// <c>ApiResults.Error(409, "insufficient_stock", "Only 2 units available.", new { available = 2 })</c>.
    /// </summary>
    public static ProblemHttpResult Error(int statusCode, string code, string message, object? details = null) =>
        TypedResults.Problem(
            statusCode: statusCode,
            detail: message,
            extensions: new Dictionary<string, object?> { [CodeKey] = code, [DetailsKey] = details });
}
