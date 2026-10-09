using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Stockroom.Api.Auth;
using Stockroom.Api.Configuration;
using Stockroom.Core.Users;
using Stockroom.Tests.Infrastructure;
using static Stockroom.Tests.Api.TestAuth;

namespace Stockroom.Tests.Api;

public sealed class LoginEndpointTests(StockroomApiFactory factory, PostgresFixture postgres) : IClassFixture<StockroomApiFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ValidCredentialsReturnAnAccessAndARefreshToken()
    {
        var user = await CreateUserAsync(factory, Roles.Staff);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(LoginUri, new { username = user.UserName, password = Password }, Token);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        var body = document.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["token_type", "access_token", "expires_in", "refresh_token"], body.EnumerateObject().Select(p => p.Name));
        Assert.Equal("Bearer", body.GetProperty("token_type").GetString());
        Assert.Equal(900, body.GetProperty("expires_in").GetInt32());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("refresh_token").GetString()));
        Assert.True((await ValidateAsync(factory, body.GetProperty("access_token").GetString()!)).IsValid);
    }

    [Fact]
    public async Task AccessTokenIdentifiesTheUserByPublicIdAndRole()
    {
        var user = await CreateUserAsync(factory, Roles.Admin);

        var tokens = await LoginAsync(factory, user.UserName!);
        var result = await ValidateAsync(factory, tokens.AccessToken);
        var jwt = new JsonWebToken(tokens.AccessToken);

        Assert.True(result.IsValid, result.Exception?.Message);
        Assert.Equal(user.PublicId.ToString(), result.Claims[JwtRegisteredClaimNames.Sub]);
        Assert.Equal(user.UserName, result.Claims[JwtRegisteredClaimNames.Name]);
        Assert.Equal(Roles.Admin, result.Claims[AccessTokenIssuer.RoleClaim]);
        Assert.Equal(TimeSpan.FromMinutes(15), jwt.ValidTo - jwt.IssuedAt);

        // Clients can read the token, so it must not reveal the internal ID.
        Assert.DoesNotContain(user.Id.ToString(), jwt.EncodedPayload + Base64UrlEncoder.Decode(jwt.EncodedPayload), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UsernameIsNotCaseSensitive()
    {
        var user = await CreateUserAsync(factory, Roles.Staff);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(LoginUri, new { username = user.UserName!.ToUpperInvariant(), password = Password }, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RefreshTokenIsStoredOnlyAsAHashWithTheDeviceName()
    {
        var user = await CreateUserAsync(factory, Roles.Staff);

        var tokens = await LoginAsync(factory, user.UserName!, deviceName: "  Anna's iPhone ");
        var stored = await RefreshTokensOfAsync(factory, user);

        var token = Assert.Single(stored);
        Assert.Equal(RefreshTokenService.Hash(tokens.RefreshToken), token.TokenHash);
        Assert.Equal("Anna's iPhone", token.DeviceName);
        Assert.Equal(7, token.Id.Version);
        Assert.Equal(4, token.PublicId.Version);
        Assert.Equal(TimeSpan.FromDays(30), token.ExpiresAt - token.CreatedAt);
    }

    [Fact]
    public async Task EachLoginGetsItsOwnRefreshToken()
    {
        var user = await CreateUserAsync(factory, Roles.Staff);

        var phone = await LoginAsync(factory, user.UserName!, deviceName: "Phone");
        var laptop = await LoginAsync(factory, user.UserName!);
        var stored = await RefreshTokensOfAsync(factory, user);

        Assert.NotEqual(phone.RefreshToken, laptop.RefreshToken);
        Assert.Equal(2, stored.Count);
        Assert.Contains(stored, t => t.DeviceName is null);
    }

    [Theory]
    [InlineData(true, "wrong passphrase")]
    [InlineData(false, Password)]
    public async Task WrongCredentialsAreRejectedTheSameWay(bool userExists, string password)
    {
        var user = await CreateUserAsync(factory, Roles.Staff);
        var username = userExists ? user.UserName : "nobody-" + user.UserName;
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(LoginUri, new { username, password }, Token);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        var error = document.RootElement.GetProperty("error");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("invalid_credentials", error.GetProperty("code").GetString());
        Assert.Equal("The username or password is incorrect.", error.GetProperty("message").GetString());
        Assert.Empty(await RefreshTokensOfAsync(factory, user));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedLoginsCheckAPasswordWhetherOrNotTheUserExists(bool userExists)
    {
        // Otherwise an unknown username would fail faster, and response times would reveal which ones exist.
        var user = await CreateUserAsync(factory, Roles.Staff);
        var hasher = new CountingPasswordHasher();
        await using var app = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IPasswordHasher<User>>(hasher)));
        using var client = app.CreateClient();
        var username = userExists ? user.UserName : "nobody-" + user.UserName;

        // Twice, so the one-off hash created for the first unknown username is not counted.
        (await client.PostAsJsonAsync(LoginUri, new { username, password = "wrong passphrase" }, Token)).Dispose();
        hasher.Verifications = 0;
        using var response = await client.PostAsJsonAsync(LoginUri, new { username, password = "wrong passphrase" }, Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, hasher.Verifications);
    }

    [Fact]
    public async Task MissingFieldsAreRejected()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(LoginUri, new { username = "" }, Token);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        var error = document.RootElement.GetProperty("error");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", error.GetProperty("code").GetString());
        Assert.Equal(["password", "username"], error.GetProperty("details").GetProperty("fields").EnumerateObject().Select(p => p.Name).Order());
    }

    [Fact]
    public void LoginIsPublic()
    {
        var endpoint = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == "/api/v1/auth/login");

        Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
    }

    [Fact]
    public async Task SigningKeyIsGeneratedPerDatabaseAndSurvivesRestarts()
    {
        var user = await CreateUserAsync(factory, Roles.Staff);
        var tokens = await LoginAsync(factory, user.UserName!);
        var databaseUrl = factory.Settings[StockroomOptions.DatabaseUrlKey];

        await using var restarted = await StockroomApiFactory.CreateAsync(postgres, configure: s => s[StockroomOptions.DatabaseUrlKey] = databaseUrl);
        await using var otherServer = await StockroomApiFactory.CreateAsync(postgres);

        Assert.True((await ValidateAsync(restarted, tokens.AccessToken)).IsValid);
        Assert.False((await ValidateAsync(otherServer, tokens.AccessToken)).IsValid);
    }

    private sealed class CountingPasswordHasher : IPasswordHasher<User>
    {
        private readonly PasswordHasher<User> _inner = new();

        public int Verifications { get; set; }

        public string HashPassword(User user, string password) => _inner.HashPassword(user, password);

        public PasswordVerificationResult VerifyHashedPassword(User user, string hashedPassword, string providedPassword)
        {
            Verifications++;
            return _inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
        }
    }
}
