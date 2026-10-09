using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Stockroom.Api.Configuration;

namespace Stockroom.Api.Security;

/// <summary>
/// Strict CORS (spec 11): browsers may call the API only from the origins in
/// <c>STOCKROOM_ALLOWED_CORS_ORIGINS</c>, none by default. A web app served behind the same reverse proxy
/// as the API needs none.
/// </summary>
/// <remarks>
/// Credentials (cookies) are not allowed: clients send the access token in the <c>Authorization</c>
/// header and the refresh token in the request body.
/// </remarks>
internal static class CorsExtensions
{
    /// <summary>How long a browser may cache a preflight response.</summary>
    public static readonly TimeSpan PreflightMaxAge = TimeSpan.FromMinutes(10);

    public static IServiceCollection AddStockroomCors(this IServiceCollection services)
    {
        services.AddCors();
        services.AddOptions<CorsOptions>().Configure<IOptions<StockroomOptions>>((cors, stockroom) =>
            cors.AddDefaultPolicy(policy => policy
                .WithOrigins([.. stockroom.Value.AllowedCorsOrigins])
                .AllowAnyMethod()
                .AllowAnyHeader()

                // Readable by scripts on other origins, so a client can wait as long as a 429 asks.
                .WithExposedHeaders(HeaderNames.RetryAfter)
                .SetPreflightMaxAge(PreflightMaxAge)));
        return services;
    }
}
