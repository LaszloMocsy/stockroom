using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Stockroom.Core.Users;
using static Stockroom.Tests.Api.TestAuth;

namespace Stockroom.Tests.Api;

public sealed class LogoutEndpointTests(StockroomApiFactory factory) : IClassFixture<StockroomApiFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LoggedOutTokenCanNoLongerRefresh()
    {
        var user = await CreateUserAsync(factory, Roles.Staff);
        var login = await LoginAsync(factory, user.UserName!);
        using var client = factory.CreateClient();

        using var logout = await client.PostAsJsonAsync(LogoutUri, new { refresh_token = login.RefreshToken }, Token);
        using var refresh = await client.PostAsJsonAsync(RefreshUri, new { refresh_token = login.RefreshToken }, Token);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Empty(await logout.Content.ReadAsByteArrayAsync(Token));
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        Assert.Equal("invalid_refresh_token", await ErrorCodeAsync(refresh));
        Assert.NotNull(Assert.Single(await RefreshTokensOfAsync(factory, user)).RevokedAt);
    }

    [Fact]
    public async Task LogoutEndsTheWholeDeviceSession()
    {
        var user = await CreateUserAsync(factory, Roles.Staff);
        var login = await LoginAsync(factory, user.UserName!);
        var refreshed = await RefreshAsync(factory, login.RefreshToken);
        using var client = factory.CreateClient();

        using var logout = await client.PostAsJsonAsync(LogoutUri, new { refresh_token = refreshed.RefreshToken }, Token);
        using var refresh = await client.PostAsJsonAsync(RefreshUri, new { refresh_token = refreshed.RefreshToken }, Token);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        Assert.All(await RefreshTokensOfAsync(factory, user), t => Assert.NotNull(t.RevokedAt));
    }

    [Fact]
    public async Task OtherDevicesStayLoggedIn()
    {
        var user = await CreateUserAsync(factory, Roles.Staff);
        var phone = await LoginAsync(factory, user.UserName!, deviceName: "Phone");
        var laptop = await LoginAsync(factory, user.UserName!, deviceName: "Laptop");
        using var client = factory.CreateClient();

        (await client.PostAsJsonAsync(LogoutUri, new { refresh_token = phone.RefreshToken }, Token)).Dispose();
        using var laptopRefresh = await client.PostAsJsonAsync(RefreshUri, new { refresh_token = laptop.RefreshToken }, Token);

        Assert.Equal(HttpStatusCode.OK, laptopRefresh.StatusCode);
    }

    [Fact]
    public async Task LogoutDuringARefreshStillEndsTheSession()
    {
        // Whichever runs first, a token issued by the refresh must not outlive the logout.
        using var client = factory.CreateClient();
        for (var i = 0; i < 25; i++)
        {
            var user = await CreateUserAsync(factory, Roles.Staff);
            var login = await LoginAsync(factory, user.UserName!);

            var refresh = client.PostAsJsonAsync(RefreshUri, new { refresh_token = login.RefreshToken }, Token);
            var logout = client.PostAsJsonAsync(LogoutUri, new { refresh_token = login.RefreshToken }, Token);
            (await refresh).Dispose();
            (await logout).Dispose();

            Assert.All(await RefreshTokensOfAsync(factory, user), t => Assert.NotNull(t.RevokedAt));
        }
    }

    [Fact]
    public async Task LogoutSucceedsForUnknownAndAlreadyRevokedTokens()
    {
        var user = await CreateUserAsync(factory, Roles.Staff);
        var login = await LoginAsync(factory, user.UserName!);
        using var client = factory.CreateClient();

        using var unknown = await client.PostAsJsonAsync(LogoutUri, new { refresh_token = "not-a-token" }, Token);
        using var first = await client.PostAsJsonAsync(LogoutUri, new { refresh_token = login.RefreshToken }, Token);
        using var second = await client.PostAsJsonAsync(LogoutUri, new { refresh_token = login.RefreshToken }, Token);

        Assert.Equal(HttpStatusCode.NoContent, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
    }

    [Fact]
    public async Task MissingTokenIsRejected()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(LogoutUri, new { }, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", await ErrorCodeAsync(response));
    }

    [Fact]
    public void LogoutIsPublic()
    {
        // Works with the refresh token alone, so a device can log out after its access token has expired.
        var endpoint = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == "/api/v1/auth/logout");

        Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
    }
}
