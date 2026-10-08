using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Stockroom.Api.Configuration;
using Stockroom.Core.Auth;
using Stockroom.Core.Users;
using Stockroom.Tests.Infrastructure;

namespace Stockroom.Tests.Api;

/// <summary>Users, logins, and token checks for tests of the auth endpoints.</summary>
public static class TestAuth
{
    /// <summary>Password of every user from <see cref="CreateUserAsync"/>.</summary>
    public const string Password = "long enough passphrase";

    public static readonly Uri LoginUri = new("/api/v1/auth/login", UriKind.Relative);

    public static readonly Uri RefreshUri = new("/api/v1/auth/refresh", UriKind.Relative);

    public static readonly Uri LogoutUri = new("/api/v1/auth/logout", UriKind.Relative);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>Creates a user with a unique username, <see cref="Password"/>, and <paramref name="role"/>.</summary>
    public static async Task<User> CreateUserAsync(StockroomApiFactory app, string role)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = new User { UserName = $"user-{Guid.NewGuid():N}", DisplayName = "Test User" };
        Assert.True((await users.CreateAsync(user, Password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        return user;
    }

    public static async Task<Tokens> LoginAsync(StockroomApiFactory app, string username, string? deviceName = null)
    {
        using var client = app.CreateClient();
        using var response = await client.PostAsJsonAsync(LoginUri, new { username, password = Password, device_name = deviceName }, Token);
        response.EnsureSuccessStatusCode();
        return await ReadTokensAsync(response);
    }

    public static async Task<Tokens> RefreshAsync(StockroomApiFactory app, string refreshToken)
    {
        using var client = app.CreateClient();
        using var response = await client.PostAsJsonAsync(RefreshUri, new { refresh_token = refreshToken }, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadTokensAsync(response);
    }

    /// <summary>The <c>error.code</c> of an error response.</summary>
    public static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return document.RootElement.GetProperty("error").GetProperty("code").GetString();
    }

    public static async Task<Tokens> ReadTokensAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return new Tokens(document.RootElement.GetProperty("access_token").GetString()!, document.RootElement.GetProperty("refresh_token").GetString()!);
    }

    /// <summary>Validates an access token exactly as the API does for an incoming Authorization header.</summary>
    public static async Task<TokenValidationResult> ValidateAsync(StockroomApiFactory app, string accessToken)
    {
        var options = app.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);
        return await options.TokenHandlers.Single().ValidateTokenAsync(accessToken, options.TokenValidationParameters);
    }

    /// <summary>The user's stored refresh tokens, oldest first.</summary>
    public static async Task<List<RefreshToken>> RefreshTokensOfAsync(StockroomApiFactory app, User user)
    {
        await using var db = TestDatabase.CreateContext(app.Settings[StockroomOptions.DatabaseUrlKey]!);
        return await db.RefreshTokens.Where(t => t.UserId == user.Id).OrderBy(t => t.CreatedAt).ToListAsync(Token);
    }
}

public sealed record Tokens(string AccessToken, string RefreshToken);
