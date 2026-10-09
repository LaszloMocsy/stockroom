using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Stockroom.Api.Auth;
using Stockroom.Api.Configuration;
using Stockroom.Core.Users;
using Stockroom.Tests.Infrastructure;
using static Stockroom.Tests.Api.TestAuth;

namespace Stockroom.Tests.Api;

public sealed class RefreshEndpointTests(StockroomApiFactory factory) : IClassFixture<StockroomApiFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RefreshReturnsNewTokensForTheSameUser()
    {
        var user = await CreateUserAsync(factory, Roles.Staff);
        var login = await LoginAsync(factory, user.UserName!);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(RefreshUri, new { refresh_token = login.RefreshToken }, Token);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        var body = document.RootElement;
        var refreshed = await ValidateAsync(factory, body.GetProperty("access_token").GetString()!);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["token_type", "access_token", "expires_in", "refresh_token"], body.EnumerateObject().Select(p => p.Name));
        Assert.Equal("Bearer", body.GetProperty("token_type").GetString());
        Assert.Equal(900, body.GetProperty("expires_in").GetInt32());
        Assert.NotEqual(login.RefreshToken, body.GetProperty("refresh_token").GetString());
        Assert.True(refreshed.IsValid, refreshed.Exception?.Message);
        Assert.Equal(user.PublicId.ToString(), refreshed.Claims[JwtRegisteredClaimNames.Sub]);
        Assert.Equal(Roles.Staff, refreshed.Claims[AccessTokenIssuer.RoleClaim]);
    }

    [Fact]
    public async Task RotationUsesUpTheOldTokenAndContinuesTheDeviceSession()
    {
        var user = await CreateUserAsync(factory, Roles.Staff);
        var login = await LoginAsync(factory, user.UserName!, deviceName: "Phone");

        var refreshed = await RefreshAsync(factory, login.RefreshToken);
        var stored = await RefreshTokensOfAsync(factory, user);

        Assert.Equal(2, stored.Count);
        var (old, current) = (stored[0], stored[1]);
        Assert.Equal(RefreshTokenService.Hash(login.RefreshToken), old.TokenHash);
        Assert.Equal(RefreshTokenService.Hash(refreshed.RefreshToken), current.TokenHash);
        Assert.NotNull(old.UsedAt);
        Assert.Null(current.UsedAt);
        Assert.Equal(old.SessionId, current.SessionId);
        Assert.Equal("Phone", current.DeviceName);
        Assert.All(stored, t => Assert.Null(t.RevokedAt));
    }

    [Fact]
    public async Task EachNewTokenCanBeRefreshedInTurn()
    {
        var user = await CreateUserAsync(factory, Roles.Staff);
        var tokens = await LoginAsync(factory, user.UserName!);

        for (var i = 0; i < 3; i++)
        {
            tokens = await RefreshAsync(factory, tokens.RefreshToken);
        }

        Assert.Equal(4, (await RefreshTokensOfAsync(factory, user)).Count);
    }

    [Fact]
    public async Task ReusingARotatedTokenRevokesTheChain()
    {
        var user = await CreateUserAsync(factory, Roles.Staff);
        var login = await LoginAsync(factory, user.UserName!);
        var refreshed = await RefreshAsync(factory, login.RefreshToken);
        using var client = factory.CreateClient();

        // The old token comes back: a stolen copy, say. Neither it nor the token that replaced it works any more.
        using var reuse = await client.PostAsJsonAsync(RefreshUri, new { refresh_token = login.RefreshToken }, Token);
        using var afterReuse = await client.PostAsJsonAsync(RefreshUri, new { refresh_token = refreshed.RefreshToken }, Token);

        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
        Assert.Equal("invalid_refresh_token", await ErrorCodeAsync(reuse));
        Assert.Equal(HttpStatusCode.Unauthorized, afterReuse.StatusCode);
        Assert.Equal("invalid_refresh_token", await ErrorCodeAsync(afterReuse));
        Assert.All(await RefreshTokensOfAsync(factory, user), t => Assert.NotNull(t.RevokedAt));
    }

    [Fact]
    public async Task ReuseRevokesOnlyThatDevicesSession()
    {
        var user = await CreateUserAsync(factory, Roles.Staff);
        var phone = await LoginAsync(factory, user.UserName!, deviceName: "Phone");
        var laptop = await LoginAsync(factory, user.UserName!, deviceName: "Laptop");
        await RefreshAsync(factory, phone.RefreshToken);
        using var client = factory.CreateClient();

        (await client.PostAsJsonAsync(RefreshUri, new { refresh_token = phone.RefreshToken }, Token)).Dispose();
        using var laptopRefresh = await client.PostAsJsonAsync(RefreshUri, new { refresh_token = laptop.RefreshToken }, Token);

        Assert.Equal(HttpStatusCode.OK, laptopRefresh.StatusCode);
    }

    [Fact]
    public async Task ConcurrentRefreshesWithOneTokenYieldAtMostOneSuccessor()
    {
        var user = await CreateUserAsync(factory, Roles.Staff);
        var login = await LoginAsync(factory, user.UserName!);
        using var client = factory.CreateClient();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            client.PostAsJsonAsync(RefreshUri, new { refresh_token = login.RefreshToken }, Token)));
        var statuses = responses.Select(r => r.StatusCode).ToList();
        foreach (var response in responses)
        {
            response.Dispose();
        }

        // One request wins the token; the others present a used token, which counts as reuse.
        Assert.Single(statuses, HttpStatusCode.OK);
        Assert.Equal(7, statuses.Count(s => s == HttpStatusCode.Unauthorized));
        var stored = await RefreshTokensOfAsync(factory, user);
        Assert.Equal(2, stored.Count);
        Assert.All(stored, t => Assert.NotNull(t.RevokedAt));
    }

    [Fact]
    public async Task ExpiredTokenIsRejected()
    {
        var user = await CreateUserAsync(factory, Roles.Staff);
        var login = await LoginAsync(factory, user.UserName!);
        await using (var db = TestDatabase.CreateContext(factory.Settings[StockroomOptions.DatabaseUrlKey]!))
        {
            await db.RefreshTokens
                .Where(t => t.UserId == user.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)), Token);
        }

        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(RefreshUri, new { refresh_token = login.RefreshToken }, Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("invalid_refresh_token", await ErrorCodeAsync(response));
        Assert.Single(await RefreshTokensOfAsync(factory, user));
    }

    [Fact]
    public async Task UnknownTokenIsRejected()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(RefreshUri, new { refresh_token = "not-a-token" }, Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("invalid_refresh_token", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task RefreshedAccessTokenCarriesTheCurrentRole()
    {
        var user = await CreateUserAsync(factory, Roles.Staff);
        var login = await LoginAsync(factory, user.UserName!);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var loaded = await users.FindByIdAsync(user.Id.ToString());
            Assert.True((await users.RemoveFromRoleAsync(loaded!, Roles.Staff)).Succeeded);
            Assert.True((await users.AddToRoleAsync(loaded!, Roles.Admin)).Succeeded);
        }

        var refreshed = await RefreshAsync(factory, login.RefreshToken);

        Assert.Equal(Roles.Admin, (await ValidateAsync(factory, refreshed.AccessToken)).Claims[AccessTokenIssuer.RoleClaim]);
    }

    [Fact]
    public async Task MissingTokenIsRejected()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(RefreshUri, new { }, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", await ErrorCodeAsync(response));
    }

    [Fact]
    public void RefreshIsPublic()
    {
        // Called when the access token has expired, so it cannot require one.
        var endpoint = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == "/api/v1/auth/refresh");

        Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
    }
}
