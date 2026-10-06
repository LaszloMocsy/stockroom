namespace Stockroom.Api.Errors;

/// <summary>The error envelope every failed request returns (spec 10): <c>{ "error": { "code", "message", "details" } }</c>.</summary>
public sealed record ErrorResponse(ApiError Error);

/// <param name="Code">Stable, machine-readable identifier such as <c>not_found</c>. Clients branch on this, never on <paramref name="Message"/>.</param>
/// <param name="Message">Human-readable explanation. May change between releases.</param>
/// <param name="Details">Optional structured data, e.g. per-field validation errors. <c>null</c> when there is nothing to add.</param>
public sealed record ApiError(string Code, string Message, object? Details);

/// <summary>Error codes that the framework layer produces. Feature code adds its own (e.g. <c>insufficient_stock</c>).</summary>
public static class ErrorCodes
{
    public const string BadRequest = "bad_request";
    public const string ValidationFailed = "validation_failed";
    public const string Unauthorized = "unauthorized";
    public const string Forbidden = "forbidden";
    public const string NotFound = "not_found";
    public const string MethodNotAllowed = "method_not_allowed";
    public const string NotAcceptable = "not_acceptable";
    public const string Conflict = "conflict";
    public const string PayloadTooLarge = "payload_too_large";
    public const string UnsupportedMediaType = "unsupported_media_type";
    public const string TooManyRequests = "too_many_requests";
    public const string InternalError = "internal_error";
    public const string ServiceUnavailable = "service_unavailable";

    /// <summary>Default code for a status that has no specific code, e.g. <c>http_418</c>.</summary>
    public static string ForStatus(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => BadRequest,
        StatusCodes.Status401Unauthorized => Unauthorized,
        StatusCodes.Status403Forbidden => Forbidden,
        StatusCodes.Status404NotFound => NotFound,
        StatusCodes.Status405MethodNotAllowed => MethodNotAllowed,
        StatusCodes.Status406NotAcceptable => NotAcceptable,
        StatusCodes.Status409Conflict => Conflict,
        StatusCodes.Status413PayloadTooLarge => PayloadTooLarge,
        StatusCodes.Status415UnsupportedMediaType => UnsupportedMediaType,
        StatusCodes.Status429TooManyRequests => TooManyRequests,
        StatusCodes.Status500InternalServerError => InternalError,
        StatusCodes.Status503ServiceUnavailable => ServiceUnavailable,
        _ => $"http_{statusCode}",
    };
}
