using System.Diagnostics;

namespace Stockroom.Api.Logging;

/// <summary>
/// Writes one structured log line per request, when it completes. Must run inside
/// <see cref="CorrelationIdMiddleware"/> so the line carries the correlation ID.
/// The query string is deliberately not logged, since it can hold secrets.
/// </summary>
internal sealed partial class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    // Polled by container health checks and load balancers; successful calls would drown out real traffic.
    private static readonly string[] ProbePaths = ["/healthz", "/readyz"];

    public async Task InvokeAsync(HttpContext context)
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            await next(context);
        }
        catch when (!context.Response.HasStarted)
        {
            // The exception handler (task B5) turns this into a response; until then the host answers 500.
            // Once the response has started the status can no longer change, so it is logged as sent.
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            throw;
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            var request = context.Request;
            var statusCode = context.Response.StatusCode;
            if (statusCode >= StatusCodes.Status500InternalServerError)
            {
                RequestFailed(logger, request.Method, request.Path.Value, statusCode, elapsedMs);
            }
            else if (statusCode < StatusCodes.Status400BadRequest && IsProbe(request.Path))
            {
                ProbeCompleted(logger, request.Method, request.Path.Value, statusCode, elapsedMs);
            }
            else
            {
                RequestCompleted(logger, request.Method, request.Path.Value, statusCode, elapsedMs);
            }
        }
    }

    private static bool IsProbe(PathString path)
    {
        // Routing ignores case and a trailing slash, so match the same way.
        var value = path.Value?.TrimEnd('/');
        return ProbePaths.Any(p => string.Equals(p, value, StringComparison.OrdinalIgnoreCase));
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "{Method} {Path} responded {StatusCode} in {ElapsedMs:0.0} ms")]
    private static partial void RequestCompleted(ILogger logger, string method, string? path, int statusCode, double elapsedMs);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "{Method} {Path} responded {StatusCode} in {ElapsedMs:0.0} ms")]
    private static partial void RequestFailed(ILogger logger, string method, string? path, int statusCode, double elapsedMs);

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug, Message = "{Method} {Path} responded {StatusCode} in {ElapsedMs:0.0} ms")]
    private static partial void ProbeCompleted(ILogger logger, string method, string? path, int statusCode, double elapsedMs);
}
