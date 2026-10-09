using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Stockroom.Api.Auth;

/// <summary>
/// Rate limit for the endpoints that check passwords or tokens (spec 10, 11): a fixed number of requests
/// per client IP address per window. Rejected requests get 429 <c>too_many_requests</c> and
/// <c>Retry-After</c>.
/// </summary>
/// <remarks>
/// Behind a reverse proxy every client has the proxy's address, so the limit applies to all clients
/// together until forwarded headers are trusted.
/// </remarks>
internal static class AuthRateLimiting
{
    public const string PolicyName = "auth";

    public static IServiceCollection AddStockroomAuthRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<AuthRateLimitOptions>();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // The body is left empty, so the status code pages write the usual error envelope.
            options.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = RetryAfterSeconds(retryAfter);
                }

                return ValueTask.CompletedTask;
            };

            options.AddPolicy(PolicyName, context =>
            {
                var limits = context.RequestServices.GetRequiredService<IOptions<AuthRateLimitOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = limits.PermitLimit, Window = limits.Window, QueueLimit = 0 });
            });
        });
        return services;
    }

    /// <summary>A <c>Retry-After</c> value in whole seconds, rounded up.</summary>
    public static string RetryAfterSeconds(TimeSpan delay) =>
        ((int)Math.Ceiling(delay.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
}

/// <summary>Limits for <see cref="AuthRateLimiting"/>. Not configurable through the environment.</summary>
internal sealed class AuthRateLimitOptions
{
    /// <summary>
    /// Allows a whole shift of devices that share one network address to log in and refresh at once,
    /// while per-account lockout (see <see cref="LoginLockout"/>) limits guessing any one password.
    /// </summary>
    public int PermitLimit { get; set; } = 30;

    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
}
