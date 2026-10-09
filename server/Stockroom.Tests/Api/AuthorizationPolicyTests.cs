using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Stockroom.Api.Auth;
using Stockroom.Core.Users;
using static Stockroom.Tests.Api.TestAuth;

namespace Stockroom.Tests.Api;

public sealed class AuthorizationPolicyTests : IAsyncLifetime
{
    private TestApi _api = null!;
    private AccessTokenIssuer _issuer = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _api = await TestApi.StartAsync(app =>
        {
            // The same convention as Program.cs: the group requires the default policy.
            var endpoints = app.MapGroup("").RequireAuthorization();
            endpoints.MapGet("/default", () => "ok");
            endpoints.MapGet("/staff", () => "ok").RequireStaff();
            endpoints.MapGet("/admin", () => "ok").RequireAdmin();
        });

        // This host has no database, so give it a signing key directly.
        var key = _api.Services.GetRequiredService<AccessTokenKey>();
        key.Load(RandomNumberGenerator.GetBytes(32));
        _issuer = new AccessTokenIssuer(key, TimeProvider.System);
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Theory]
    [InlineData("/default", Roles.Staff, HttpStatusCode.OK)]
    [InlineData("/default", Roles.Admin, HttpStatusCode.OK)]
    [InlineData("/staff", Roles.Staff, HttpStatusCode.OK)]
    [InlineData("/staff", Roles.Admin, HttpStatusCode.OK)]
    [InlineData("/admin", Roles.Admin, HttpStatusCode.OK)]
    [InlineData("/admin", Roles.Staff, HttpStatusCode.Forbidden)]
    public async Task RolesAreChecked(string path, string role, HttpStatusCode expected)
    {
        using var response = await GetAsync(path, _issuer.Issue(NewUser(), [role]));

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.Forbidden)
        {
            Assert.Equal("forbidden", await ErrorCodeAsync(response));
        }
    }

    [Theory]
    [InlineData("/default")]
    [InlineData("/staff")]
    [InlineData("/admin")]
    public async Task AnonymousRequestsAreUnauthorized(string path)
    {
        using var response = await GetAsync(path, accessToken: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", await ErrorCodeAsync(response));
    }

    [Theory]
    [InlineData("/default")]
    [InlineData("/staff")]
    public async Task UsersWithoutARoleAreForbidden(string path)
    {
        using var response = await GetAsync(path, _issuer.Issue(NewUser(), []));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static User NewUser() => new() { UserName = "anna", DisplayName = "Anna", PublicId = Guid.NewGuid() };

    private async Task<HttpResponseMessage> GetAsync(string path, string? accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return await _api.Client.SendAsync(request, Token);
    }
}
