namespace Stockroom.Api.Logging;

internal static class LoggingExtensions
{
    /// <summary>Writes logs to the console as one JSON object per line, in UTC.</summary>
    public static ILoggingBuilder AddStockroomLogging(this ILoggingBuilder logging)
    {
        logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.UseUtcTimestamp = true;
            options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
        });

        // RequestLoggingMiddleware writes the one line per request, so keep the framework's own
        // per-request lines (host diagnostics, endpoint execution) out. Set in code so it holds
        // even without appsettings.json; warnings and errors from the framework still appear.
        logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        return logging;
    }

    /// <summary>Adds the correlation ID and request logging middleware. Call it first in the pipeline.</summary>
    public static IApplicationBuilder UseStockroomRequestLogging(this IApplicationBuilder app) =>
        app.UseMiddleware<CorrelationIdMiddleware>().UseMiddleware<RequestLoggingMiddleware>();
}
