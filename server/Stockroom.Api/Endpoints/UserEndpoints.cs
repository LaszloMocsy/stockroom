using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Stockroom.Api.Auth;
using Stockroom.Api.Errors;
using Stockroom.Core.Users;
using Stockroom.Data;

namespace Stockroom.Api.Endpoints;

/// <summary><c>/api/v1/users</c>: user administration, for ADMIN users only (spec 2.1, 4.7).</summary>
internal static class UserEndpoints
{
    public const string LastAdmin = "last_admin";

    // Serialises demotions, so two requests cannot each demote one of the last two ADMINs.
    // Any constant works as long as nothing else uses it; this one is "AdminLck" in ASCII.
    private const long DemotionLockKey = 0x41646d696e4c636b;

    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var users = endpoints.MapGroup("/users").WithTags("Users").RequireAdmin();

        users.MapGet("", ListUsersAsync)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .WithName("ListUsers")
            .WithSummary("List users")
            .WithDescription("Returns every user, sorted by username ignoring case. ADMIN only. Not paged yet: everything comes in one page and `next_cursor` is always `null`.");

        users.MapPost("", CreateUserAsync)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .WithName("CreateUser")
            .WithSummary("Create a user")
            .WithDescription("Creates a STAFF user, or an ADMIN user when `role` is `ADMIN`. ADMIN only. A username that is already taken, ignoring case, is reported as a `username` validation error.");

        users.MapPatch("/{id:guid}", UpdateUserAsync)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .WithName("UpdateUser")
            .WithSummary("Update a user")
            .WithDescription($"Changes a user's display name, role, or password. Fields that are omitted or `null` stay as they are. ADMIN only. Setting a password logs the user out on every device and clears any login lockout. Making the last ADMIN a STAFF user returns 409 with `{LastAdmin}`.");
        return endpoints;
    }

    private static async Task<Ok<ListResponse<UserResponse>>> ListUsersAsync(StockroomDbContext db, CancellationToken cancellationToken)
    {
        var items = await (
                from user in db.Users
                join userRole in db.UserRoles on user.Id equals userRole.UserId
                join role in db.Roles on userRole.RoleId equals role.Id
                orderby user.NormalizedUserName
                select new UserResponse(user.PublicId, user.UserName!, user.DisplayName, role.Name!))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new ListResponse<UserResponse>(items, NextCursor: null));
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

    private static async Task<Results<Ok<UserResponse>, ProblemHttpResult>> UpdateUserAsync(
        Guid id,
        UpdateUserRequest request,
        StockroomDbContext db,
        UserManager<User> users,
        RefreshTokenService refreshTokens,
        CancellationToken cancellationToken)
    {
        // [Required] would reject a blank name, but also an omitted one, which here means "unchanged".
        if (request.DisplayName is not null && string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return ApiResults.ValidationFailed(new Dictionary<string, string[]> { [nameof(request.DisplayName)] = ["The display name cannot be blank."] });
        }

        // Everything below commits together or not at all, e.g. a role change with a rejected password.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (request.Role == Roles.Staff)
        {
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({DemotionLockKey})", cancellationToken);
        }

        // Locks the row, so concurrent changes to one user apply in turn instead of failing Identity's
        // concurrency check. Not FOR UPDATE: that would also block a refresh adding a token for this user
        // (its foreign key check) while it holds the session lock a password reset waits for: a deadlock.
        var user = await db.Users
            .FromSql($"SELECT * FROM users WHERE public_id = {id} FOR NO KEY UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            return ApiResults.Error(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "There is no user with this ID.");
        }

        var role = await UserAccounts.RoleOfAsync(users, user);
        if (request.Role is not null && request.Role != role)
        {
            if (role == Roles.Admin && await db.UserRoles.CountAsync(r => r.RoleId == Roles.AdminId, cancellationToken) == 1)
            {
                return ApiResults.Error(StatusCodes.Status409Conflict, LastAdmin, "The last ADMIN cannot be made STAFF. Make another user ADMIN first.");
            }

            UserAccounts.EnsureSucceeded(await users.RemoveFromRoleAsync(user, role), $"remove user {user.PublicId} from {role}");
            UserAccounts.EnsureSucceeded(await users.AddToRoleAsync(user, request.Role), $"add user {user.PublicId} to {request.Role}");
            role = request.Role;
        }

        if (request.DisplayName is not null)
        {
            user.DisplayName = request.DisplayName;
        }

        if (request.Password is not null)
        {
            UserAccounts.EnsureSucceeded(await users.RemovePasswordAsync(user), $"remove the password of user {user.PublicId}");
            var passwordSet = await users.AddPasswordAsync(user, request.Password);
            if (!passwordSet.Succeeded)
            {
                return UserAccounts.ValidationFailed(passwordSet);
            }

            // Whoever knew the old password, a lost device or an earlier guesser, is shut out (spec 10.2);
            // the user starts with a clean lockout count.
            user.AccessFailedCount = 0;
            user.LockoutEnd = null;
            await refreshTokens.RevokeAllSessionsAsync(user.Id, cancellationToken);
        }

        UserAccounts.EnsureSucceeded(await users.UpdateAsync(user), $"update user {user.PublicId}");
        await transaction.CommitAsync(cancellationToken);

        return TypedResults.Ok(UserResponse.From(user, role));
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

/// <summary>Request body for <c>PATCH /api/v1/users/{id}</c>. Fields that are omitted or <c>null</c> stay as they are.</summary>
/// <param name="DisplayName">New name shown in the apps.</param>
/// <param name="Role"><c>STAFF</c> or <c>ADMIN</c>. The last ADMIN cannot be made STAFF.</param>
/// <param name="Password">New password, at least 8 characters. Logs the user out on every device.</param>
public sealed record UpdateUserRequest(
    [property: StringLength(100)] string? DisplayName = null,
    [property: AllowedValues(null, Roles.Staff, Roles.Admin)] string? Role = null,
    [property: StringLength(128)] string? Password = null);
