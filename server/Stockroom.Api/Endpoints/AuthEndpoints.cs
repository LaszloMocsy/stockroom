using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Stockroom.Api.Auth;
using Stockroom.Api.Errors;
using Stockroom.Core.Users;

namespace Stockroom.Api.Endpoints;

/// <summary><c>/api/v1/auth</c>: login and, later, token refresh and logout (spec 10.2).</summary>
internal static class AuthEndpoints
{
    public const string InvalidCredentials = "invalid_credentials";

    // Hash of a random password, created on the first login attempt for an unknown username.
    private static string? UnknownUserHash;

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/auth").WithTags("Auth");

        auth.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .WithName("Login")
            .WithSummary("Log in with username and password")
            .WithDescription($"Returns a short-lived access token for the `Authorization: Bearer` header and a refresh token for this device. Wrong credentials return 401 with `{InvalidCredentials}`, whether or not the username exists.");
        return endpoints;
    }

    private static async Task<Results<Ok<TokenResponse>, ProblemHttpResult>> LoginAsync(
        LoginRequest request,
        UserManager<User> users,
        AccessTokenIssuer accessTokens,
        RefreshTokenIssuer refreshTokens,
        CancellationToken cancellationToken)
    {
        var user = await users.FindByNameAsync(request.Username);
        if (user is null)
        {
            // Hash anyway, so an unknown username takes as long as a wrong password and the response
            // time does not reveal which usernames exist.
            UnknownUserHash ??= users.PasswordHasher.HashPassword(new User { DisplayName = "" }, Guid.NewGuid().ToString());
            users.PasswordHasher.VerifyHashedPassword(new User { DisplayName = "" }, UnknownUserHash, request.Password);
        }

        if (user is null || !await users.CheckPasswordAsync(user, request.Password))
        {
            return ApiResults.Error(StatusCodes.Status401Unauthorized, InvalidCredentials, "The username or password is incorrect.");
        }

        var deviceName = string.IsNullOrWhiteSpace(request.DeviceName) ? null : request.DeviceName.Trim();
        var refreshToken = await refreshTokens.IssueAsync(user, deviceName, cancellationToken);
        var accessToken = accessTokens.Issue(user, await users.GetRolesAsync(user));

        return TypedResults.Ok(new TokenResponse("Bearer", accessToken, (int)AccessTokenIssuer.Lifetime.TotalSeconds, refreshToken));
    }
}

/// <summary>Request body for <c>POST /api/v1/auth/login</c>.</summary>
/// <param name="Username">Login name; case does not matter.</param>
/// <param name="Password">The user's password.</param>
/// <param name="DeviceName">Optional label for this device, such as "Anna's iPhone", to tell sessions apart.</param>
public sealed record LoginRequest(
    [property: Required, StringLength(64)] string Username,
    [property: Required, StringLength(128)] string Password,
    [property: StringLength(100)] string? DeviceName = null);

/// <summary>Tokens for an authenticated device.</summary>
/// <param name="TokenType">Always <c>Bearer</c>.</param>
/// <param name="AccessToken">Send as <c>Authorization: Bearer &lt;access_token&gt;</c>.</param>
/// <param name="ExpiresIn">Seconds until the access token expires.</param>
/// <param name="RefreshToken">Exchanges for new tokens when the access token expires. Store it securely.</param>
public sealed record TokenResponse(string TokenType, string AccessToken, int ExpiresIn, string RefreshToken);
