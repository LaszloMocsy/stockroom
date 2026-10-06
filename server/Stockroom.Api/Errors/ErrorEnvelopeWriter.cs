using Microsoft.AspNetCore.Http.Json;
using ProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Stockroom.Api.Errors;

/// <summary>
/// Replaces the RFC 7807 problem-details body with the <see cref="ErrorResponse"/> envelope.
/// Everything in ASP.NET Core that reports an error (the exception handler, status code pages,
/// request binding failures, validation, <see cref="TypedResults.Problem(string?, string?, int?, string?, string?, IDictionary{string, object?}?)"/>)
/// goes through <see cref="IProblemDetailsService"/>, so this is the single place the envelope is written.
/// </summary>
internal sealed class ErrorEnvelopeWriter(IOptions<JsonOptions> jsonOptions) : IProblemDetailsWriter
{
    private const string InternalErrorMessage = "An unexpected error occurred.";

    // The API only speaks JSON, so the envelope is written whatever the Accept header says.
    public bool CanWrite(ProblemDetailsContext context) => true;

    public ValueTask WriteAsync(ProblemDetailsContext context)
    {
        var response = context.HttpContext.Response;
        var problem = context.ProblemDetails;
        var statusCode = problem.Status ?? response.StatusCode;

        response.StatusCode = statusCode;
        var envelope = new ErrorResponse(ToApiError(problem, statusCode));
        return new ValueTask(response.WriteAsJsonAsync(envelope, jsonOptions.Value.SerializerOptions, "application/json", context.HttpContext.RequestAborted));
    }

    private ApiError ToApiError(ProblemDetails problem, int statusCode)
    {
        // Explicit errors from ApiResults.Error carry their own code, message, and details.
        if (problem.Extensions.TryGetValue(ApiResults.CodeKey, out var code) && code is string explicitCode)
        {
            problem.Extensions.TryGetValue(ApiResults.DetailsKey, out var details);
            return new ApiError(explicitCode, problem.Detail ?? DefaultMessage(problem, statusCode), details);
        }

        if (problem is HttpValidationProblemDetails validation)
        {
            var policy = jsonOptions.Value.SerializerOptions.PropertyNamingPolicy;
            var fields = validation.Errors.ToDictionary(e => FieldName(e.Key, policy), e => e.Value);
            return new ApiError(ErrorCodes.ValidationFailed, "One or more fields are invalid.", new { Fields = fields });
        }

        // Server errors never echo exception text or framework details back to the client.
        var message = statusCode >= StatusCodes.Status500InternalServerError ? InternalErrorMessage : DefaultMessage(problem, statusCode);
        return new ApiError(ErrorCodes.ForStatus(statusCode), message, Details: null);
    }

    private static string DefaultMessage(ProblemDetails problem, int statusCode) =>
        problem.Detail ?? problem.Title ?? ReasonPhrases.GetReasonPhrase(statusCode);

    // Field names follow the JSON naming policy, so "TargetQuantity" is reported as "target_quantity".
    // Nested paths such as "Lines[0].Quantity" are converted segment by segment.
    private static string FieldName(string key, System.Text.Json.JsonNamingPolicy? policy) =>
        policy is null || key.Length == 0
            ? key
            : string.Join('.', key.Split('.').Select(segment =>
            {
                var bracket = segment.IndexOf('[', StringComparison.Ordinal);
                return bracket < 0
                    ? policy.ConvertName(segment)
                    : policy.ConvertName(segment[..bracket]) + segment[bracket..];
            }));
}
