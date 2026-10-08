using System.Reflection;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Stockroom.Data;

namespace Stockroom.Api.Endpoints;

/// <summary>
/// <c>GET /api/v1/info</c>: what clients check on connect to decide whether they can talk to this server
/// (spec 10.1). Public, because it is called before login.
/// </summary>
internal static class InfoEndpoints
{
    /// <summary>
    /// Version of the <c>/api/v1</c> contract as <c>major.minor</c>. The major matches the URL; the minor
    /// increases with additive changes, so clients can detect features newer than themselves.
    /// </summary>
    public const string ApiVersion = "1.0";

    /// <summary>Oldest client app version this server supports. <c>0.0.0</c> means any version.</summary>
    public const string MinClientVersion = "0.0.0";

    /// <summary>The server's semantic version, without build metadata such as a commit hash.</summary>
    public static string ServerVersion { get; } = ReadServerVersion();

    public static IEndpointRouteBuilder MapInfoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/info", GetInfoAsync)
            .AllowAnonymous()
            .WithName("GetInfo")
            .WithTags("Server")
            .WithSummary("Server and API version")
            .WithDescription("Versions for the client compatibility check, and whether first-run setup is still required. Does not require authentication.");
        return endpoints;
    }

    private static async Task<Ok<InfoResponse>> GetInfoAsync(StockroomDbContext db, CancellationToken cancellationToken)
    {
        // Setup stays required until the first account exists; POST /api/v1/setup creates it.
        var setupRequired = !await db.Users.AnyAsync(cancellationToken);
        return TypedResults.Ok(new InfoResponse(ServerVersion, ApiVersion, MinClientVersion, setupRequired));
    }

    private static string ReadServerVersion()
    {
        var version = typeof(InfoEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? throw new InvalidOperationException("The API assembly has no informational version.");
        var metadata = version.IndexOf('+', StringComparison.Ordinal);
        return metadata < 0 ? version : version[..metadata];
    }
}

/// <param name="ServerVersion">Semantic version of this server, e.g. <c>0.1.0</c>.</param>
/// <param name="ApiVersion">Version of the <c>/api/v1</c> contract as <c>major.minor</c>, e.g. <c>1.0</c>.</param>
/// <param name="MinClientVersion">Oldest supported client app version; older apps must ask the user to update.</param>
/// <param name="SetupRequired">True until the first ADMIN account has been created through <c>POST /api/v1/setup</c>.</param>
public sealed record InfoResponse(string ServerVersion, string ApiVersion, string MinClientVersion, bool SetupRequired);
