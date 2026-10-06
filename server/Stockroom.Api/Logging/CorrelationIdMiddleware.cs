namespace Stockroom.Api.Logging;

/// <summary>
/// Gives every request a correlation ID: the caller's <c>X-Correlation-Id</c> when it is well formed,
/// otherwise a new one. The ID is echoed in the response header, stored as
/// <see cref="HttpContext.TraceIdentifier"/>, and attached to every log line written during the request.
/// </summary>
internal sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ScopeKey = "CorrelationId";

    private const int MaxLength = 64;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault() is { } supplied && IsValid(supplied)
            ? supplied
            : Guid.NewGuid().ToString("N");

        context.TraceIdentifier = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new CorrelationScope(correlationId)))
        {
            await next(context);
        }
    }

    // Client-supplied, so restrict it to a short, log-safe alphabet.
    private static bool IsValid(string value) =>
        value.Length is > 0 and <= MaxLength
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
}

/// <summary>Log scope that the JSON formatter writes as <c>{ "Message": "CorrelationId:…", "CorrelationId": "…" }</c>.</summary>
file sealed class CorrelationScope(string correlationId) : IReadOnlyList<KeyValuePair<string, object>>
{
    public int Count => 1;

    public KeyValuePair<string, object> this[int index] =>
        index == 0 ? new(CorrelationIdMiddleware.ScopeKey, correlationId) : throw new ArgumentOutOfRangeException(nameof(index));

    public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
    {
        yield return this[0];
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => $"{CorrelationIdMiddleware.ScopeKey}:{correlationId}";
}
