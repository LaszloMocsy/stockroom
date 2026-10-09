using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Stockroom.Api.Auth;
using Stockroom.Api.Errors;
using Stockroom.Core.Users;

namespace Stockroom.Api.Endpoints;

/// <summary><c>/api/v1/auth</c>: login, token refresh, and logout (spec 10.2).</summary>
internal static class AuthEndpoints
{
    public const string InvalidCredentials = "invalid_credentials";
    public const string InvalidRefreshToken = "invalid_refresh_token";
    public const string AccountLockedOut = "account_locked_out";

    // Hash of a random password, created on the first login attempt for an unknown username.
    private static string? UnknownUserHash;

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/auth")
            .WithTags("Auth")
            .RequireRateLimiting(AuthRateLimiting.PolicyName);

        auth.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status429TooManyRequests)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .WithName("Login")
            .WithSummary("Log in with username and password")
            .WithDescription($"Returns a short-lived access token for the `Authorization: Bearer` header and a refresh token for this device. Wrong credentials return 401 with `{InvalidCredentials}`, whether or not the username exists. {LoginLockout.FreeAttempts} wrong passwords in a row are allowed; the next one locks the account, for longer after each further failure, and login returns 429 with `{AccountLockedOut}` and `Retry-After` until the lockout ends.");

        auth.MapPost("/refresh", RefreshAsync)
            .AllowAnonymous()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status429TooManyRequests)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .WithName("RefreshTokens")
            .WithSummary("Exchange a refresh token for new tokens")
            .WithDescription($"Returns a new access token and a new refresh token; the refresh token sent is used up. Sending a used refresh token again ends the device's session, so every token from that login stops working. An unknown, expired, used, or revoked token returns 401 with `{InvalidRefreshToken}`, and the client has to log in again.");

        auth.MapPost("/logout", LogoutAsync)
            .AllowAnonymous()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status429TooManyRequests)
            .WithName("Logout")
            .WithSummary("Log this device out")
            .WithDescription("Revokes the device's session, so its refresh token can no longer be used. Takes the refresh token rather than the access token, so it works after the access token has expired. Always returns 204, also for an unknown or already revoked token. The access token stays valid until it expires (at most 15 minutes); clients discard it.");
        return endpoints;
    }

    private static async Task<Results<Ok<TokenResponse>, ProblemHttpResult>> LoginAsync(
        LoginRequest request,
        UserManager<User> users,
        LoginLockout lockout,
        AccessTokenIssuer accessTokens,
        RefreshTokenService refreshTokens,
        HttpContext httpContext,
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
        else if (lockout.RemainingLockout(user) is { } remaining)
        {
            // The password is not checked, so guessing it gets nowhere while the account is locked.
            httpContext.Response.Headers.RetryAfter = AuthRateLimiting.RetryAfterSeconds(remaining);
            return ApiResults.Error(
                StatusCodes.Status429TooManyRequests,
                AccountLockedOut,
                "Too many failed logins for this account. Try again later.",
                new { RetryAfterSeconds = (int)Math.Ceiling(remaining.TotalSeconds) });
        }

        if (user is null || !await users.CheckPasswordAsync(user, request.Password))
        {
            if (user is not null)
            {
                await lockout.RecordFailureAsync(user, cancellationToken);
            }

            return ApiResults.Error(StatusCodes.Status401Unauthorized, InvalidCredentials, "The username or password is incorrect.");
        }

        await lockout.RecordSuccessAsync(user, cancellationToken);

        var deviceName = string.IsNullOrWhiteSpace(request.DeviceName) ? null : request.DeviceName.Trim();
        var refreshToken = await refreshTokens.IssueAsync(user, deviceName, cancellationToken);
        if (refreshToken is null)
        {
            // An ADMIN reset the password (or deleted the user) since it was checked.
            return ApiResults.Error(StatusCodes.Status401Unauthorized, InvalidCredentials, "The username or password is incorrect.");
        }

        return TypedResults.Ok(await TokensAsync(user, refreshToken, users, accessTokens));
    }

    private static async Task<Results<Ok<TokenResponse>, ProblemHttpResult>> RefreshAsync(
        RefreshRequest request,
        UserManager<User> users,
        AccessTokenIssuer accessTokens,
        RefreshTokenService refreshTokens,
        CancellationToken cancellationToken)
    {
        var rotation = await refreshTokens.RotateAsync(request.RefreshToken, cancellationToken);
        var user = rotation is null ? null : await users.FindByIdAsync(rotation.UserId.ToString());
        if (rotation is null || user is null)
        {
            return ApiResults.Error(StatusCodes.Status401Unauthorized, InvalidRefreshToken, "The refresh token is invalid, expired, or revoked. Log in again.");
        }

        // Roles are read again, so a role change takes effect at the next refresh.
        return TypedResults.Ok(await TokensAsync(user, rotation.RefreshToken, users, accessTokens));
    }

    private static async Task<NoContent> LogoutAsync(
        LogoutRequest request,
        RefreshTokenService refreshTokens,
        CancellationToken cancellationToken)
    {
        await refreshTokens.RevokeSessionAsync(request.RefreshToken, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<TokenResponse> TokensAsync(User user, string refreshToken, UserManager<User> users, AccessTokenIssuer accessTokens)
    {
        var accessToken = accessTokens.Issue(user, await users.GetRolesAsync(user));
        return new TokenResponse("Bearer", accessToken, (int)AccessTokenIssuer.Lifetime.TotalSeconds, refreshToken);
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

/// <summary>Request body for <c>POST /api/v1/auth/refresh</c>.</summary>
/// <param name="RefreshToken">The refresh token from the last login or refresh.</param>
public sealed record RefreshRequest([property: Required, StringLength(256)] string RefreshToken);

/// <summary>Request body for <c>POST /api/v1/auth/logout</c>.</summary>
/// <param name="RefreshToken">The device's current refresh token.</param>
public sealed record LogoutRequest([property: Required, StringLength(256)] string RefreshToken);

/// <summary>Tokens for an authenticated device.</summary>
/// <param name="TokenType">Always <c>Bearer</c>.</param>
/// <param name="AccessToken">Send as <c>Authorization: Bearer &lt;access_token&gt;</c>.</param>
/// <param name="ExpiresIn">Seconds until the access token expires.</param>
/// <param name="RefreshToken">Exchanges for new tokens when the access token expires; works once. Store it securely.</param>
public sealed record TokenResponse(string TokenType, string AccessToken, int ExpiresIn, string RefreshToken);
