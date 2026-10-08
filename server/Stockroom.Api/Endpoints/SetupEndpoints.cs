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

        var user = new User { UserName = request.Username, DisplayName = request.DisplayName };
        var created = await users.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            return ApiResults.ValidationFailed(IdentityErrorFields(created.Errors));
        }

        var addedToRole = await users.AddToRoleAsync(user, Roles.Admin);
        if (!addedToRole.Succeeded)
        {
            throw new InvalidOperationException($"Could not add the first user to {Roles.Admin}: {string.Join(" ", addedToRole.Errors.Select(e => e.Description))}");
        }

        await transaction.CommitAsync(cancellationToken);

        // No Location header: there is no endpoint for a single user, and /me needs the user to log in first.
        return TypedResults.Created((string?)null, UserResponse.From(user, Roles.Admin));
    }

    // Identity reports username and password problems as codes such as "PasswordTooShort" and "InvalidUserName".
    private static Dictionary<string, string[]> IdentityErrorFields(IEnumerable<IdentityError> errors) =>
        errors
            .GroupBy(e => e.Code.StartsWith("Password", StringComparison.Ordinal) ? "password" : "username")
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());
}

/// <summary>Request body for <c>POST /api/v1/setup</c>.</summary>
/// <param name="Username">Login name for the first ADMIN. Letters, digits, and <c>-._@+</c>.</param>
/// <param name="DisplayName">Name shown in the apps.</param>
/// <param name="Password">At least 8 characters.</param>
public sealed record SetupRequest(
    [property: Required, StringLength(64)] string Username,
    [property: Required, StringLength(100)] string DisplayName,
    [property: Required, StringLength(128)] string Password);
