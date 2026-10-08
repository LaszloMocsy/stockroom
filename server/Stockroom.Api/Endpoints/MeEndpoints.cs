using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Stockroom.Api.Auth;
using Stockroom.Api.Errors;
using Stockroom.Core.Users;

namespace Stockroom.Api.Endpoints;

/// <summary><c>GET /api/v1/me</c>: the user the access token belongs to (spec 10).</summary>
internal static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/me", GetMeAsync)
            .RequireAuthorization()
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .WithName("GetMe")
            .WithTags("Users")
            .WithSummary("The current user")
            .WithDescription("Returns the authenticated user, with the role clients use to decide which controls to show. Read from the database, so it reflects changes made since the access token was issued.");
        return endpoints;
    }

    private static async Task<Results<Ok<UserResponse>, ProblemHttpResult>> GetMeAsync(
        ClaimsPrincipal principal,
        UserManager<User> users,
        CancellationToken cancellationToken)
    {
        var publicId = principal.UserPublicId();
        var user = publicId is null ? null : await users.Users.SingleOrDefaultAsync(u => u.PublicId == publicId, cancellationToken);
        if (user is null)
        {
            // A valid token whose user no longer exists.
            return ApiResults.Error(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized, "The user for this access token no longer exists.");
        }

        return TypedResults.Ok(await UserResponse.FromAsync(user, users));
    }
}
