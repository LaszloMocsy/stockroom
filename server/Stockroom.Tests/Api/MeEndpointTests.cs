using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Stockroom.Api.Auth;
using Stockroom.Core.Users;
using static Stockroom.Tests.Api.TestAuth;

namespace Stockroom.Tests.Api;

public sealed class MeEndpointTests(StockroomApiFactory factory) : IClassFixture<StockroomApiFactory>
{
    private static readonly Uri MeUri = new("/api/v1/me", UriKind.Relative);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(Roles.Staff)]
    [InlineData(Roles.Admin)]
    public async Task MeReturnsTheAuthenticatedUser(string role)
    {
        var user = await CreateUserAsync(factory, role);
        var tokens = await LoginAsync(factory, user.UserName!);

        using var response = await GetMeAsync(tokens.AccessToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        var body = document.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["id", "username", "display_name", "role"], body.EnumerateObject().Select(p => p.Name));
        Assert.Equal(user.PublicId, body.GetProperty("id").GetGuid());
        Assert.Equal(user.UserName, body.GetProperty("username").GetString());
        Assert.Equal(user.DisplayName, body.GetProperty("display_name").GetString());
        Assert.Equal(role, body.GetProperty("role").GetString());
    }

    [Fact]
    public async Task MeReflectsChangesMadeAfterLogin()
    {
        var user = await CreateUserAsync(factory, Roles.Staff);
        var tokens = await LoginAsync(factory, user.UserName!);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var loaded = await users.FindByIdAsync(user.Id.ToString());
            loaded!.DisplayName = "Renamed";
            Assert.True((await users.UpdateAsync(loaded)).Succeeded);
        }

        using var response = await GetMeAsync(tokens.AccessToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));

        Assert.Equal("Renamed", document.RootElement.GetProperty("display_name").GetString());
    }

    [Fact]
    public async Task MeRequiresAnAccessToken()
    {
        using var response = await GetMeAsync(accessToken: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
        Assert.Equal("unauthorized", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task ExpiredAccessTokenIsRejected()
    {
        var user = await CreateUserAsync(factory, Roles.Staff);
        var key = factory.Services.GetRequiredService<AccessTokenKey>();
        var issuedAnHourAgo = new AccessTokenIssuer(key, new FakeTimeProvider(DateTimeOffset.UtcNow.AddHours(-1)));

        using var response = await GetMeAsync(issuedAnHourAgo.Issue(user, [Roles.Staff]));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", await ErrorCodeAsync(response));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ForgedOrMalformedAccessTokenIsRejected(bool forged)
    {
        var user = await CreateUserAsync(factory, Roles.Admin);
        var otherKey = new AccessTokenKey();
        otherKey.Load(RandomNumberGenerator.GetBytes(32));
        var token = forged ? new AccessTokenIssuer(otherKey, TimeProvider.System).Issue(user, [Roles.Admin]) : "not-a-jwt";

        using var response = await GetMeAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task TokenOfADeletedUserIsRejected()
    {
        var user = await CreateUserAsync(factory, Roles.Staff);
        var tokens = await LoginAsync(factory, user.UserName!);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            Assert.True((await users.DeleteAsync((await users.FindByIdAsync(user.Id.ToString()))!)).Succeeded);
        }

        using var response = await GetMeAsync(tokens.AccessToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", await ErrorCodeAsync(response));
    }

    private async Task<HttpResponseMessage> GetMeAsync(string? accessToken)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, MeUri);
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return await client.SendAsync(request, Token);
    }
}
