using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Stockroom.Api.Auth;
using Stockroom.Api.Configuration;
using Stockroom.Core.Users;
using Stockroom.Tests.Infrastructure;
using static Stockroom.Tests.Api.TestAuth;

namespace Stockroom.Tests.Api;

/// <summary>Lockout backoff and rate limiting on the auth endpoints (spec 10, 11).</summary>
public sealed class LoginProtectionTests(StockroomApiFactory factory) : IClassFixture<StockroomApiFactory>
{
    private const string WrongPassword = "wrong passphrase";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(1, null)]
    [InlineData(4, null)]
    [InlineData(5, 1)]
    [InlineData(6, 2)]
    [InlineData(7, 4)]
    [InlineData(8, 8)]
    [InlineData(9, 15)]
    [InlineData(1000, 15)]
    public void LockoutGrowsWithEachFailureUpToALimit(int failures, int? minutes)
    {
        Assert.Equal(minutes is null ? null : TimeSpan.FromMinutes(minutes.Value), LoginLockout.LockoutAfter(failures));
    }

    [Fact]
    public async Task RepeatedWrongPasswordsLockTheAccountForLongerEachTime()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var app = WithServices(services => services.AddSingleton<TimeProvider>(time));
        var user = await CreateUserAsync(app, Roles.Staff);
        using var client = app.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, user, WrongPassword)).StatusCode);
        }

        // Locked for a minute: even the right password is refused, without being checked.
        await AssertLockedOutAsync(client, user, retryAfterSeconds: 60);

        time.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, user, WrongPassword)).StatusCode);
        await AssertLockedOutAsync(client, user, retryAfterSeconds: 120);

        time.Advance(TimeSpan.FromSeconds(121));
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, user, Password)).StatusCode);

        // The successful login reset the count, so one more mistake does not lock the account.
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, user, WrongPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, user, Password)).StatusCode);
    }

    [Fact]
    public async Task SuccessfulLoginResetsTheFailureCount()
    {
        var user = await CreateUserAsync(factory, Roles.Staff);
        using var client = factory.CreateClient();

        for (var round = 0; round < 2; round++)
        {
            for (var i = 0; i < LoginLockout.FreeAttempts; i++)
            {
                (await LoginAsync(client, user, WrongPassword)).Dispose();
            }

            using var response = await LoginAsync(client, user, Password);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task LockoutAffectsOnlyThatAccount()
    {
        var locked = await CreateUserAsync(factory, Roles.Staff);
        var other = await CreateUserAsync(factory, Roles.Staff);
        using var client = factory.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            (await LoginAsync(client, locked, WrongPassword)).Dispose();
        }

        using var lockedLogin = await LoginAsync(client, locked, Password);
        using var otherLogin = await LoginAsync(client, other, Password);

        Assert.Equal(HttpStatusCode.TooManyRequests, lockedLogin.StatusCode);
        Assert.Equal(HttpStatusCode.OK, otherLogin.StatusCode);
    }

    [Fact]
    public async Task ConcurrentWrongPasswordsAreAllCounted()
    {
        var user = await CreateUserAsync(factory, Roles.Staff);
        using var client = factory.CreateClient();

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => LoginAsync(client, user, WrongPassword)));
        var rejected = responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized);
        foreach (var response in responses)
        {
            response.Dispose();
        }

        // Requests that arrived after the lockout started were refused without counting (429).
        await using var db = TestDatabase.CreateContext(factory.Settings[StockroomOptions.DatabaseUrlKey]!);
        var failures = await db.Users.Where(u => u.Id == user.Id).Select(u => u.AccessFailedCount).SingleAsync(Token);
        Assert.Equal(rejected, failures);
        Assert.True(failures >= 5, $"Only {failures} failures were counted.");
    }

    [Fact]
    public async Task AuthEndpointsAreRateLimitedPerClient()
    {
        await using var app = WithServices(services => services.Configure<AuthRateLimitOptions>(o => o.PermitLimit = 3));
        using var client = app.CreateClient();

        // Login, refresh, logout, and setup share one budget.
        using var login = await client.PostAsJsonAsync(LoginUri, new { username = "nobody", password = WrongPassword }, Token);
        using var refresh = await client.PostAsJsonAsync(RefreshUri, new { refresh_token = "not-a-token" }, Token);
        using var logout = await client.PostAsJsonAsync(LogoutUri, new { refresh_token = "not-a-token" }, Token);
        using var limitedSetup = await client.PostAsJsonAsync(
            new Uri("/api/v1/setup", UriKind.Relative), new { username = "anna", display_name = "Anna", password = Password }, Token);
        using var limitedLogin = await client.PostAsJsonAsync(LoginUri, new { username = "nobody", password = WrongPassword }, Token);
        using var info = await client.GetAsync(new Uri("/api/v1/info", UriKind.Relative), Token);

        Assert.Equal(
            [HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.NoContent],
            new[] { login, refresh, logout }.Select(r => r.StatusCode));
        foreach (var limited in new[] { limitedSetup, limitedLogin })
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
            Assert.Equal("too_many_requests", await ErrorCodeAsync(limited));
            Assert.InRange(limited.Headers.RetryAfter?.Delta?.TotalSeconds ?? 0, 1, 60);
        }

        Assert.Equal(HttpStatusCode.OK, info.StatusCode);
    }

    private WebApplicationFactory<Program> WithServices(Action<IServiceCollection> configure) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(configure));

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, User user, string password) =>
        client.PostAsJsonAsync(LoginUri, new { username = user.UserName, password }, Token);

    private static async Task AssertLockedOutAsync(HttpClient client, User user, int retryAfterSeconds)
    {
        using var response = await LoginAsync(client, user, Password);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        var error = document.RootElement.GetProperty("error");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("account_locked_out", error.GetProperty("code").GetString());
        Assert.Equal(retryAfterSeconds, error.GetProperty("details").GetProperty("retry_after_seconds").GetInt32());
        Assert.Equal(TimeSpan.FromSeconds(retryAfterSeconds), response.Headers.RetryAfter?.Delta);
    }
}
