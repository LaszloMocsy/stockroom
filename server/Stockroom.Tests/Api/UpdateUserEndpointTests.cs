using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stockroom.Api.Auth;
using Stockroom.Api.Configuration;
using Stockroom.Core.Auth;
using Stockroom.Core.Users;
using Stockroom.Data;
using Stockroom.Tests.Infrastructure;
using static Stockroom.Tests.Api.TestAuth;

namespace Stockroom.Tests.Api;

public sealed class UpdateUserEndpointTests(StockroomApiFactory factory, PostgresFixture postgres) : IClassFixture<StockroomApiFactory>
{
    private const string NewPassword = "a brand new passphrase";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AdminChangesTheDisplayName()
    {
        using var admin = await CreateClientAsAsync(factory, Roles.Admin);
        var user = await CreateUserAsync(factory, Roles.Staff);

        using var response = await admin.PatchAsJsonAsync(UserUri(user), new { display_name = "Renamed User" }, Token);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        var body = document.RootElement;
        var (stored, role) = await LoadAsync(factory, user);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["id", "username", "display_name", "role"], body.EnumerateObject().Select(p => p.Name));
        Assert.Equal(user.PublicId, body.GetProperty("id").GetGuid());
        Assert.Equal(user.UserName, body.GetProperty("username").GetString());
        Assert.Equal("Renamed User", body.GetProperty("display_name").GetString());
        Assert.Equal(Roles.Staff, body.GetProperty("role").GetString());
        Assert.Equal("Renamed User", stored.DisplayName);
        Assert.Equal(Roles.Staff, role);
    }

    [Theory]
    [InlineData(Roles.Staff, Roles.Admin)]
    [InlineData(Roles.Admin, Roles.Staff)]
    public async Task AdminChangesTheRole(string from, string to)
    {
        using var admin = await CreateClientAsAsync(factory, Roles.Admin);
        var user = await CreateUserAsync(factory, from);
        var login = await LoginAsync(factory, user.UserName!);

        using var response = await admin.PatchAsJsonAsync(UserUri(user), new { role = to }, Token);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        var refreshed = await RefreshAsync(factory, login.RefreshToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(to, document.RootElement.GetProperty("role").GetString());
        Assert.Equal(to, (await LoadAsync(factory, user)).Role);

        // A role change does not log the user out; their next access token carries the new role.
        Assert.Equal(to, (await ValidateAsync(factory, refreshed.AccessToken)).Claims["role"]);
    }

    [Fact]
    public async Task OmittedFieldsStayUnchanged()
    {
        using var admin = await CreateClientAsAsync(factory, Roles.Admin);
        var user = await CreateUserAsync(factory, Roles.Admin);
        var login = await LoginAsync(factory, user.UserName!);

        using var response = await admin.PatchAsJsonAsync(UserUri(user), new { display_name = (string?)null }, Token);
        var (stored, role) = await LoadAsync(factory, user);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(user.DisplayName, stored.DisplayName);
        Assert.Equal(Roles.Admin, role);
        Assert.Equal(PasswordVerificationResult.Success, VerifyPassword(stored, Password));
        Assert.All(await RefreshTokensOfAsync(factory, user), t => Assert.Null(t.RevokedAt));
        await RefreshAsync(factory, login.RefreshToken);
    }

    [Fact]
    public async Task PasswordResetReplacesThePasswordAndLogsOutEveryDevice()
    {
        using var admin = await CreateClientAsAsync(factory, Roles.Admin);
        var user = await CreateUserAsync(factory, Roles.Staff);
        var phone = await LoginAsync(factory, user.UserName!, deviceName: "Phone");
        var laptop = await LoginAsync(factory, user.UserName!, deviceName: "Laptop");
        await RefreshAsync(factory, phone.RefreshToken);

        using var response = await admin.PatchAsJsonAsync(UserUri(user), new { password = NewPassword }, Token);
        using var client = factory.CreateClient();
        using var laptopRefresh = await client.PostAsJsonAsync(RefreshUri, new { refresh_token = laptop.RefreshToken }, Token);
        using var oldPassword = await client.PostAsJsonAsync(LoginUri, new { username = user.UserName, password = Password }, Token);
        using var newPassword = await client.PostAsJsonAsync(LoginUri, new { username = user.UserName, password = NewPassword }, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, laptopRefresh.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newPassword.StatusCode);

        // Every token from before the reset is revoked; only the new login's token is not.
        var stored = await RefreshTokensOfAsync(factory, user);
        Assert.Equal(4, stored.Count);
        Assert.All(stored[..3], t => Assert.NotNull(t.RevokedAt));
        Assert.Null(stored[3].RevokedAt);
    }

    [Fact]
    public async Task PasswordResetClearsALockout()
    {
        using var admin = await CreateClientAsAsync(factory, Roles.Admin);
        var user = await CreateUserAsync(factory, Roles.Staff);
        await using (var db = TestDatabase.CreateContext(factory.Settings[StockroomOptions.DatabaseUrlKey]!))
        {
            await db.Users
                .Where(u => u.Id == user.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.AccessFailedCount, 9).SetProperty(u => u.LockoutEnd, DateTimeOffset.UtcNow.AddMinutes(15)), Token);
        }

        using var response = await admin.PatchAsJsonAsync(UserUri(user), new { password = NewPassword }, Token);
        using var client = factory.CreateClient();
        using var login = await client.PostAsJsonAsync(LoginUri, new { username = user.UserName, password = NewPassword }, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task ARejectedFieldLeavesTheWholeUserUnchanged()
    {
        using var admin = await CreateClientAsAsync(factory, Roles.Admin);
        var user = await CreateUserAsync(factory, Roles.Staff);
        var login = await LoginAsync(factory, user.UserName!);

        using var response = await admin.PatchAsJsonAsync(UserUri(user), new { display_name = "Renamed", role = Roles.Admin, password = "short" }, Token);
        var (stored, role) = await LoadAsync(factory, user);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["password"], await ValidationFieldsAsync(response));
        Assert.Equal(user.DisplayName, stored.DisplayName);
        Assert.Equal(Roles.Staff, role);
        Assert.Equal(PasswordVerificationResult.Success, VerifyPassword(stored, Password));
        await RefreshAsync(factory, login.RefreshToken);
    }

    [Theory]
    [InlineData("""{ "display_name": " " }""", "display_name")]
    [InlineData("""{ "display_name": "" }""", "display_name")]
    [InlineData("""{ "role": "staff" }""", "role")]
    [InlineData("""{ "role": "OWNER" }""", "role")]
    public async Task InvalidValuesAreRejected(string json, string field)
    {
        using var admin = await CreateClientAsAsync(factory, Roles.Admin);
        var user = await CreateUserAsync(factory, Roles.Staff);

        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        using var response = await admin.PatchAsync(UserUri(user), content, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([field], await ValidationFieldsAsync(response));
        Assert.Equal(user.DisplayName, (await LoadAsync(factory, user)).User.DisplayName);
    }

    [Fact]
    public async Task UnknownUserIsNotFound()
    {
        using var admin = await CreateClientAsAsync(factory, Roles.Admin);

        using var unknown = await admin.PatchAsJsonAsync(new Uri($"/api/v1/users/{Guid.NewGuid()}", UriKind.Relative), new { display_name = "Nobody" }, Token);
        using var notAnId = await admin.PatchAsJsonAsync(new Uri("/api/v1/users/anna", UriKind.Relative), new { display_name = "Nobody" }, Token);

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("not_found", await ErrorCodeAsync(unknown));
        Assert.Equal(HttpStatusCode.NotFound, notAnId.StatusCode);
    }

    [Fact]
    public async Task UsersAreAddressedByPublicIdOnly()
    {
        using var admin = await CreateClientAsAsync(factory, Roles.Admin);
        var user = await CreateUserAsync(factory, Roles.Staff);

        using var response = await admin.PatchAsJsonAsync(new Uri($"/api/v1/users/{user.Id}", UriKind.Relative), new { display_name = "Renamed" }, Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(user.DisplayName, (await LoadAsync(factory, user)).User.DisplayName);
    }

    [Fact]
    public async Task StaffCannotUpdateUsers()
    {
        using var staff = await CreateClientAsAsync(factory, Roles.Staff);
        var user = await CreateUserAsync(factory, Roles.Staff);

        using var response = await staff.PatchAsJsonAsync(UserUri(user), new { role = Roles.Admin, password = NewPassword }, Token);
        var (stored, role) = await LoadAsync(factory, user);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("forbidden", await ErrorCodeAsync(response));
        Assert.Equal(Roles.Staff, role);
        Assert.Equal(PasswordVerificationResult.Success, VerifyPassword(stored, Password));
    }

    [Fact]
    public async Task TheLastAdminCannotBeDemoted()
    {
        // Its own database, so this ADMIN really is the only one.
        await using var app = await StockroomApiFactory.CreateAsync(postgres);
        var only = await CreateUserAsync(app, Roles.Admin);
        using var admin = await ClientAsync(app, only);

        using var response = await admin.PatchAsJsonAsync(UserUri(only), new { display_name = "Renamed", role = Roles.Staff }, Token);
        var (stored, role) = await LoadAsync(app, only);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("last_admin", await ErrorCodeAsync(response));
        Assert.Equal(Roles.Admin, role);
        Assert.Equal(only.DisplayName, stored.DisplayName);
    }

    [Fact]
    public async Task AnAdminCanBeDemotedWhileAnotherRemains()
    {
        await using var app = await StockroomApiFactory.CreateAsync(postgres);
        var first = await CreateUserAsync(app, Roles.Admin);
        var second = await CreateUserAsync(app, Roles.Admin);
        using var admin = await ClientAsync(app, first);

        // Setting an ADMIN to ADMIN is not a demotion, so it succeeds even for the last one.
        using var demoteSelf = await admin.PatchAsJsonAsync(UserUri(first), new { role = Roles.Staff }, Token);
        using var demoteLast = await admin.PatchAsJsonAsync(UserUri(second), new { role = Roles.Staff }, Token);
        using var keepLast = await admin.PatchAsJsonAsync(UserUri(second), new { role = Roles.Admin }, Token);

        Assert.Equal(HttpStatusCode.OK, demoteSelf.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, demoteLast.StatusCode);
        Assert.Equal(HttpStatusCode.OK, keepLast.StatusCode);
        Assert.Equal(Roles.Staff, (await LoadAsync(app, first)).Role);
        Assert.Equal(Roles.Admin, (await LoadAsync(app, second)).Role);
    }

    [Fact]
    public async Task ConcurrentDemotionsLeaveOneAdmin()
    {
        await using var app = await StockroomApiFactory.CreateAsync(postgres);
        var admins = new List<User>();
        for (var i = 0; i < 6; i++)
        {
            admins.Add(await CreateUserAsync(app, Roles.Admin));
        }

        // The caller's access token still says ADMIN after its own demotion, so it can send them all.
        using var client = await ClientAsync(app, admins[0]);

        var responses = await Task.WhenAll(admins.Select(a => client.PatchAsJsonAsync(UserUri(a), new { role = Roles.Staff }, Token)));
        var statuses = responses.Select(r => r.StatusCode).ToList();
        foreach (var response in responses)
        {
            response.Dispose();
        }

        var roles = await Task.WhenAll(admins.Select(async a => (await LoadAsync(app, a)).Role));
        Assert.Equal(5, statuses.Count(s => s == HttpStatusCode.OK));
        Assert.Single(statuses, HttpStatusCode.Conflict);
        Assert.Single(roles, Roles.Admin);
    }

    [Fact]
    public async Task PasswordResetRevokesTheTokenOfARefreshInProgress()
    {
        using var admin = await CreateClientAsAsync(factory, Roles.Admin);
        var user = await CreateUserAsync(factory, Roles.Staff);
        await LoginAsync(factory, user.UserName!);
        var first = Assert.Single(await RefreshTokensOfAsync(factory, user));

        // A refresh halfway through: it holds its session's lock and has added, not yet committed, the next token.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StockroomDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(Token);
        await scope.ServiceProvider.GetRequiredService<RefreshTokenService>().LockSessionAsync(first.SessionId, Token);
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            PublicId = Guid.NewGuid(),
            UserId = user.Id,
            SessionId = first.SessionId,
            TokenHash = RefreshTokenService.Hash("next token"),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
        });
        await db.SaveChangesAsync(Token);

        var reset = admin.PatchAsJsonAsync(UserUri(user), new { password = NewPassword }, Token);
        await Task.WhenAny(reset, Task.Delay(TimeSpan.FromSeconds(1), Token));
        var finishedEarly = reset.IsCompleted;
        await transaction.CommitAsync(Token);
        using var response = await reset;

        Assert.False(finishedEarly, "The reset did not wait for the refresh in progress.");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = await RefreshTokensOfAsync(factory, user);
        Assert.Equal(2, stored.Count);
        Assert.All(stored, t => Assert.NotNull(t.RevokedAt));
    }

    [Fact]
    public async Task ARefreshDuringAPasswordResetDoesNotDeadlock()
    {
        using var hasher = new GatedPasswordHasher();
        await using var app = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IPasswordHasher<User>>(hasher)));
        using var admin = await CreateClientAsAsync(app, Roles.Admin);
        var user = await CreateUserAsync(app, Roles.Staff);
        var login = await LoginAsync(app, user.UserName!);
        using var client = app.CreateClient();

        // The reset stops while hashing the new password, after it has locked the user, and the refresh runs then.
        hasher.Close(GatedPasswordHasher.Step.Hash);
        var reset = admin.PatchAsJsonAsync(UserUri(user), new { password = NewPassword }, Token);
        await hasher.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
        var refresh = client.PostAsJsonAsync(RefreshUri, new { refresh_token = login.RefreshToken }, Token);
        await Task.WhenAny(refresh, Task.Delay(TimeSpan.FromSeconds(1), Token));
        hasher.Open();
        using var refreshResponse = await refresh;
        using var resetResponse = await reset;

        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);
        Assert.All(await RefreshTokensOfAsync(factory, user), t => Assert.NotNull(t.RevokedAt));
    }

    [Fact]
    public async Task ALoginThatCheckedTheOldPasswordDuringAResetIsRejected()
    {
        using var hasher = new GatedPasswordHasher();
        await using var app = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IPasswordHasher<User>>(hasher)));
        using var admin = await CreateClientAsAsync(app, Roles.Admin);
        var user = await CreateUserAsync(app, Roles.Staff);
        using var client = app.CreateClient();

        // The login has accepted the old password but not yet started a session when the reset commits.
        hasher.Close(GatedPasswordHasher.Step.Verify);
        var login = client.PostAsJsonAsync(LoginUri, new { username = user.UserName, password = Password }, Token);
        await hasher.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
        using var reset = await admin.PatchAsJsonAsync(UserUri(user), new { password = NewPassword }, Token);
        hasher.Open();
        using var loginResponse = await login;

        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, loginResponse.StatusCode);
        Assert.Equal("invalid_credentials", await ErrorCodeAsync(loginResponse));
        Assert.Empty(await RefreshTokensOfAsync(factory, user));
    }

    private static Uri UserUri(User user) => new($"/api/v1/users/{user.PublicId}", UriKind.Relative);

    private static async Task<HttpClient> ClientAsync(StockroomApiFactory app, User user)
    {
        var tokens = await LoginAsync(app, user.UserName!);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.AccessToken);
        return client;
    }

    private static async Task<(User User, string Role)> LoadAsync(StockroomApiFactory app, User user)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var stored = await users.FindByIdAsync(user.Id.ToString());
        Assert.NotNull(stored);
        return (stored, Assert.Single(await users.GetRolesAsync(stored)));
    }

    private static PasswordVerificationResult VerifyPassword(User user, string password) =>
        new PasswordHasher<User>().VerifyHashedPassword(user, user.PasswordHash!, password);

    private static async Task<List<string>> ValidationFieldsAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        var error = document.RootElement.GetProperty("error");
        Assert.Equal("validation_failed", error.GetProperty("code").GetString());
        return error.GetProperty("details").GetProperty("fields").EnumerateObject().Select(p => p.Name).ToList();
    }

    /// <summary>Holds up hashing or verifying a password while closed, so a test can act in the middle of a request.</summary>
    private sealed class GatedPasswordHasher : IPasswordHasher<User>, IDisposable
    {
        private readonly PasswordHasher<User> _inner = new();
        private readonly ManualResetEventSlim _gate = new(initialState: true);
        private Step _gated;

        public enum Step
        {
            Hash,
            Verify,
        }

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Close(Step step)
        {
            _gated = step;
            _gate.Reset();
        }

        public void Open() => _gate.Set();

        public string HashPassword(User user, string password)
        {
            Wait(Step.Hash);
            return _inner.HashPassword(user, password);
        }

        public PasswordVerificationResult VerifyHashedPassword(User user, string hashedPassword, string providedPassword)
        {
            // Verified first, so the request holds a result based on the hash it read.
            var result = _inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
            Wait(Step.Verify);
            return result;
        }

        public void Dispose() => _gate.Dispose();

        private void Wait(Step step)
        {
            if (!_gate.IsSet && _gated == step)
            {
                Entered.TrySetResult();
                Assert.True(_gate.Wait(TimeSpan.FromSeconds(30)), "The gate was never opened.");
            }
        }
    }
}
