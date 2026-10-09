using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Stockroom.Core.Users;
using static Stockroom.Tests.Api.TestAuth;

namespace Stockroom.Tests.Api;

public sealed class UserEndpointTests(StockroomApiFactory factory) : IClassFixture<StockroomApiFactory>
{
    private static readonly Uri UsersUri = new("/api/v1/users", UriKind.Relative);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AdminCreatesAStaffUserByDefault()
    {
        using var admin = await CreateClientAsAsync(factory, Roles.Admin);
        var username = NewUsername();

        using var response = await admin.PostAsJsonAsync(UsersUri, new { username, display_name = "Bela Nagy", password = Password }, Token);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        var body = document.RootElement;
        var user = await FindUserAsync(username);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(["id", "username", "display_name", "role"], body.EnumerateObject().Select(p => p.Name));
        Assert.Equal(username, body.GetProperty("username").GetString());
        Assert.Equal("Bela Nagy", body.GetProperty("display_name").GetString());
        Assert.Equal(Roles.Staff, body.GetProperty("role").GetString());
        Assert.NotNull(user);
        Assert.Equal(user.PublicId, body.GetProperty("id").GetGuid());
    }

    [Theory]
    [InlineData(Roles.Staff)]
    [InlineData(Roles.Admin)]
    public async Task CreatedUsersCanLogInWithTheirRole(string role)
    {
        using var admin = await CreateClientAsAsync(factory, Roles.Admin);
        var username = NewUsername();

        using var response = await admin.PostAsJsonAsync(UsersUri, new { username, display_name = "New User", password = Password, role }, Token);
        var tokens = await LoginAsync(factory, username);
        var result = await ValidateAsync(factory, tokens.AccessToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(role, result.Claims["role"]);
    }

    [Fact]
    public async Task StaffCannotCreateUsers()
    {
        using var staff = await CreateClientAsAsync(factory, Roles.Staff);
        var username = NewUsername();

        using var response = await staff.PostAsJsonAsync(UsersUri, new { username, display_name = "Sneaky", password = Password, role = Roles.Admin }, Token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("forbidden", await ErrorCodeAsync(response));
        Assert.Null(await FindUserAsync(username));
    }

    [Fact]
    public async Task AnonymousCallersCannotCreateUsers()
    {
        using var client = factory.CreateClient();
        var username = NewUsername();

        using var response = await client.PostAsJsonAsync(UsersUri, new { username, display_name = "Sneaky", password = Password }, Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(await FindUserAsync(username));
    }

    [Fact]
    public async Task TakenUsernameIsRejectedIgnoringCase()
    {
        using var admin = await CreateClientAsAsync(factory, Roles.Admin);
        var username = NewUsername();
        (await admin.PostAsJsonAsync(UsersUri, new { username, display_name = "First", password = Password }, Token)).Dispose();

        using var response = await admin.PostAsJsonAsync(UsersUri, new { username = username.ToUpperInvariant(), display_name = "Second", password = Password }, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["username"], await ValidationFieldsAsync(response));
    }

    [Fact]
    public async Task ConcurrentCreatesOfOneUsernameCreateExactlyOneUser()
    {
        using var admin = await CreateClientAsAsync(factory, Roles.Admin);
        var username = NewUsername();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => admin.PostAsJsonAsync(
            UsersUri, new { username, display_name = $"User {i}", password = Password }, Token)));
        var rejected = responses.Where(r => r.StatusCode != HttpStatusCode.Created).ToList();
        var created = responses.Length - rejected.Count;
        var rejectedFields = new List<List<string>>();
        foreach (var response in rejected)
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            rejectedFields.Add(await ValidationFieldsAsync(response));
        }

        foreach (var response in responses)
        {
            response.Dispose();
        }

        Assert.Equal(1, created);
        Assert.All(rejectedFields, fields => Assert.Equal(["username"], fields));
    }

    [Theory]
    [InlineData("short", "STAFF", "password")]
    [InlineData(Password, "OWNER", "role")]
    [InlineData(Password, "staff", "role")]
    public async Task InvalidRequestsAreRejectedWithoutCreatingTheUser(string password, string role, string field)
    {
        using var admin = await CreateClientAsAsync(factory, Roles.Admin);
        var username = NewUsername();

        using var response = await admin.PostAsJsonAsync(UsersUri, new { username, display_name = "New User", password, role }, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([field], await ValidationFieldsAsync(response));
        Assert.Null(await FindUserAsync(username));
    }

    [Fact]
    public async Task MissingFieldsAreRejected()
    {
        using var admin = await CreateClientAsAsync(factory, Roles.Admin);

        using var response = await admin.PostAsJsonAsync(UsersUri, new { }, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["display_name", "password", "username"], (await ValidationFieldsAsync(response)).Order());
    }

    private static string NewUsername() => $"user-{Guid.NewGuid():N}";

    private async Task<User?> FindUserAsync(string username)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<User>>().FindByNameAsync(username);
    }

    private static async Task<List<string>> ValidationFieldsAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        var error = document.RootElement.GetProperty("error");
        Assert.Equal("validation_failed", error.GetProperty("code").GetString());
        return error.GetProperty("details").GetProperty("fields").EnumerateObject().Select(p => p.Name).ToList();
    }
}
