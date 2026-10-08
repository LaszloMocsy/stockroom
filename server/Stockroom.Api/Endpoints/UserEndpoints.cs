using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Stockroom.Api.Auth;
using Stockroom.Api.Errors;
using Stockroom.Core.Users;
using Stockroom.Data;

namespace Stockroom.Api.Endpoints;

/// <summary><c>/api/v1/users</c>: user administration, for ADMIN users only (spec 2.1, 4.7).</summary>
internal static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var users = endpoints.MapGroup("/users").WithTags("Users").RequireAdmin();

        users.MapPost("", CreateUserAsync)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .WithName("CreateUser")
            .WithSummary("Create a user")
            .WithDescription("Creates a STAFF user, or an ADMIN user when `role` is `ADMIN`. ADMIN only. A username that is already taken, ignoring case, is reported as a `username` validation error.");
        return endpoints;
    }

    private static async Task<Results<Created<UserResponse>, ProblemHttpResult>> CreateUserAsync(
        CreateUserRequest request,
        StockroomDbContext db,
        UserManager<User> users,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var (user, error) = await UserAccounts.CreateAsync(users, request.Username, request.DisplayName, request.Password, request.Role);
        if (error is not null)
        {
            return error;
        }

        await transaction.CommitAsync(cancellationToken);

        // No Location header: users are listed and edited through /users, not fetched one at a time.
        return TypedResults.Created((string?)null, UserResponse.From(user!, request.Role));
    }
}

/// <summary>Request body for <c>POST /api/v1/users</c>.</summary>
/// <param name="Username">Login name, unique ignoring case. Letters, digits, and <c>-._@+</c>.</param>
/// <param name="DisplayName">Name shown in the apps.</param>
/// <param name="Password">Initial password, at least 8 characters.</param>
/// <param name="Role"><c>STAFF</c> (the default) or <c>ADMIN</c>.</param>
public sealed record CreateUserRequest(
    [property: Required, StringLength(64)] string Username,
    [property: Required, StringLength(100)] string DisplayName,
    [property: Required, StringLength(128)] string Password,
    [property: AllowedValues(Roles.Staff, Roles.Admin)] string Role = Roles.Staff);
