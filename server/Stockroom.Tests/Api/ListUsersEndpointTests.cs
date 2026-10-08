using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Stockroom.Core.Users;
using Stockroom.Tests.Infrastructure;
using static Stockroom.Tests.Api.TestAuth;

namespace Stockroom.Tests.Api;

public sealed class ListUsersEndpointTests(StockroomApiFactory factory, PostgresFixture postgres) : IClassFixture<StockroomApiFactory>
{
    private static readonly Uri UsersUri = new("/api/v1/users", UriKind.Relative);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AdminListsEveryUserWithTheirRoleSortedByUsername()
    {
        // Its own database, so the list holds exactly the users created here.
        await using var app = await StockroomApiFactory.CreateAsync(postgres);
        var carol = await AddUserAsync(app, "carol", "Carol Szabó", Roles.Staff);
        var anna = await AddUserAsync(app, "Anna", "Anna Kovács", Roles.Admin);
        var bob = await AddUserAsync(app, "bob", "Bob Tóth", Roles.Staff);
        var tokens = await LoginAsync(app, "Anna");
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.AccessToken);

        using var response = await client.GetAsync(UsersUri, Token);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        var body = document.RootElement;
        var items = body.GetProperty("items").EnumerateArray().ToList();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["items", "next_cursor"], body.EnumerateObject().Select(p => p.Name));
        Assert.Equal(JsonValueKind.Null, body.GetProperty("next_cursor").ValueKind);
        Assert.Equal(["Anna", "bob", "carol"], items.Select(i => i.GetProperty("username").GetString()));
        Assert.All(items, i => Assert.Equal(["id", "username", "display_name", "role"], i.EnumerateObject().Select(p => p.Name)));
        Assert.Equal([anna.PublicId, bob.PublicId, carol.PublicId], items.Select(i => i.GetProperty("id").GetGuid()));
        Assert.Equal(["Anna Kovács", "Bob Tóth", "Carol Szabó"], items.Select(i => i.GetProperty("display_name").GetString()));
        Assert.Equal([Roles.Admin, Roles.Staff, Roles.Staff], items.Select(i => i.GetProperty("role").GetString()));
    }

    [Fact]
    public async Task StaffCannotListUsers()
    {
        using var staff = await CreateClientAsAsync(factory, Roles.Staff);

        using var response = await staff.GetAsync(UsersUri, Token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("forbidden", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task AnonymousCallersCannotListUsers()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(UsersUri, Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<User> AddUserAsync(StockroomApiFactory app, string username, string displayName, string role)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = new User { UserName = username, DisplayName = displayName };
        Assert.True((await users.CreateAsync(user, Password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        return user;
    }
}
