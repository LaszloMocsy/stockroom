using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Stockroom.Api.Auth;
using Stockroom.Api.Errors;
using Stockroom.Core.Users;
using Stockroom.Data;

namespace Stockroom.Api.Endpoints;

/// <summary>
/// <c>POST /api/v1/setup</c>: first-run setup, which creates the initial ADMIN account (spec 4.7, 10).
/// Public, because no account exists yet; it is refused as soon as any user exists.
/// </summary>
internal static class SetupEndpoints
{
    public const string SetupAlreadyCompleted = "setup_already_completed";

    // Serialises concurrent setup requests, so two of them cannot both see an empty users table.
    // Any constant works as long as nothing else uses it; this one is "Stockroo" in ASCII.
    private const long SetupLockKey = 0x53746f636b726f6f;

    public static IEndpointRouteBuilder MapSetupEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/setup", SetupAsync)
            .AllowAnonymous()
            .RequireRateLimiting(AuthRateLimiting.PolicyName)
            .Produces<ErrorResponse>(StatusCodes.Status429TooManyRequests)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .WithName("Setup")
            .WithTags("Server")
            .WithSummary("Create the first ADMIN account")
            .WithDescription($"First-run setup. Only allowed while the server has no users (see `setup_required` in `/api/v1/info`); afterwards it returns 409 with `{SetupAlreadyCompleted}`. Does not require authentication and does not log the new user in.");
        return endpoints;
    }

    private static async Task<Results<Created<UserResponse>, ProblemHttpResult>> SetupAsync(
        SetupRequest request,
        StockroomDbContext db,
        UserManager<User> users,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({SetupLockKey})", cancellationToken);

        if (await db.Users.AnyAsync(cancellationToken))
        {
            return ApiResults.Error(StatusCodes.Status409Conflict, SetupAlreadyCompleted, "Setup has already been completed.");
        }

        var (user, error) = await UserAccounts.CreateAsync(users, request.Username, request.DisplayName, request.Password, Roles.Admin);
        if (error is not null)
        {
            return error;
        }

        await transaction.CommitAsync(cancellationToken);

        // No Location header: there is no endpoint for a single user, and /me needs the user to log in first.
        return TypedResults.Created((string?)null, UserResponse.From(user!, Roles.Admin));
    }
}

/// <summary>Request body for <c>POST /api/v1/setup</c>.</summary>
/// <param name="Username">Login name for the first ADMIN. Letters, digits, and <c>-._@+</c>.</param>
/// <param name="DisplayName">Name shown in the apps.</param>
/// <param name="Password">At least 8 characters.</param>
public sealed record SetupRequest(
    [property: Required, StringLength(64)] string Username,
    [property: Required, StringLength(100)] string DisplayName,
    [property: Required, StringLength(128)] string Password);
