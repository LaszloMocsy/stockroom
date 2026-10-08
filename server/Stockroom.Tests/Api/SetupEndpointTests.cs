using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stockroom.Api.Configuration;
using Stockroom.Core.Users;
using Stockroom.Tests.Data;
using Stockroom.Tests.Infrastructure;

namespace Stockroom.Tests.Api;

// Every test gets its own API and database, because setup only works once per database.
public sealed class SetupEndpointTests(PostgresFixture postgres)
{
    private static readonly Uri SetupUri = new("/api/v1/setup", UriKind.Relative);

    private static readonly object ValidRequest = new { username = "anna", display_name = "Anna Kovács", password = "long enough passphrase" };

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SetupCreatesTheFirstAdmin()
    {
        await using var factory = await StockroomApiFactory.CreateAsync(postgres);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(SetupUri, ValidRequest, Token);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        var body = document.RootElement;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal(["id", "username", "display_name", "role"], body.EnumerateObject().Select(p => p.Name));
        Assert.Equal("anna", body.GetProperty("username").GetString());
        Assert.Equal("Anna Kovács", body.GetProperty("display_name").GetString());
        Assert.Equal(Roles.Admin, body.GetProperty("role").GetString());

        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await users.FindByNameAsync("anna");
        Assert.NotNull(user);
        Assert.Equal(user.PublicId, body.GetProperty("id").GetGuid());
        Assert.True(await users.CheckPasswordAsync(user, "long enough passphrase"));
        Assert.Equal([Roles.Admin], await users.GetRolesAsync(user));
    }

    [Fact]
    public async Task SetupIsNoLongerRequiredAfterwards()
    {
        await using var factory = await StockroomApiFactory.CreateAsync(postgres);
        using var client = factory.CreateClient();

        (await client.PostAsJsonAsync(SetupUri, ValidRequest, Token)).Dispose();
        using var info = JsonDocument.Parse(await client.GetStringAsync(new Uri("/api/v1/info", UriKind.Relative), Token));

        Assert.False(info.RootElement.GetProperty("setup_required").GetBoolean());
    }

    [Fact]
    public async Task SecondSetupReturnsConflict()
    {
        await using var factory = await StockroomApiFactory.CreateAsync(postgres);
        using var client = factory.CreateClient();

        using var first = await client.PostAsJsonAsync(SetupUri, ValidRequest, Token);
        using var second = await client.PostAsJsonAsync(
            SetupUri, new { username = "mallory", display_name = "Mallory", password = "another passphrase" }, Token);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("setup_already_completed", await ErrorCodeAsync(second));
        Assert.Equal(["anna"], await UserNamesAsync(factory));
    }

    [Fact]
    public async Task SetupIsRejectedOnceAnyUserExists()
    {
        await using var factory = await StockroomApiFactory.CreateAsync(postgres);
        using var client = factory.CreateClient();
        (await client.GetAsync(new Uri("/healthz", UriKind.Relative), Token)).Dispose(); // starts the API, which migrates
        await TestDatabase.AddAsync(factory.Settings[StockroomOptions.DatabaseUrlKey]!, TestUsers.New("staff"));

        using var response = await client.PostAsJsonAsync(SetupUri, ValidRequest, Token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("setup_already_completed", await ErrorCodeAsync(response));
        Assert.Equal(["staff"], await UserNamesAsync(factory));
    }

    [Fact]
    public async Task ConcurrentSetupsCreateExactlyOneAdmin()
    {
        await using var factory = await StockroomApiFactory.CreateAsync(postgres);
        using var client = factory.CreateClient();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => client.PostAsJsonAsync(
            SetupUri, new { username = $"admin{i}", display_name = $"Admin {i}", password = "long enough passphrase" }, Token)));
        var statuses = responses.Select(r => r.StatusCode).ToList();
        foreach (var response in responses)
        {
            response.Dispose();
        }

        Assert.Single(statuses, HttpStatusCode.Created);
        Assert.All(statuses.Where(s => s != HttpStatusCode.Created), s => Assert.Equal(HttpStatusCode.Conflict, s));
        Assert.Single(await UserNamesAsync(factory));
    }

    [Fact]
    public async Task MissingFieldsAreRejected()
    {
        await using var factory = await StockroomApiFactory.CreateAsync(postgres);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(SetupUri, new { username = "", display_name = " " }, Token);
        var fields = await ValidationFieldsAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["display_name", "password", "username"], fields.EnumerateObject().Select(p => p.Name).Order());
        Assert.Empty(await UserNamesAsync(factory));
    }

    [Theory]
    [InlineData("anna", "short", "password")]
    [InlineData("anna kovács", "long enough passphrase", "username")]
    public async Task InvalidCredentialsAreRejectedWithoutCreatingTheUser(string username, string password, string field)
    {
        await using var factory = await StockroomApiFactory.CreateAsync(postgres);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(SetupUri, new { username, display_name = "Anna", password }, Token);
        var fields = await ValidationFieldsAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([field], fields.EnumerateObject().Select(p => p.Name));
        Assert.NotEmpty(fields.GetProperty(field).EnumerateArray());
        Assert.Empty(await UserNamesAsync(factory));
    }

    [Fact]
    public async Task SetupIsPublic()
    {
        await using var factory = await StockroomApiFactory.CreateAsync(postgres);

        var endpoint = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == "/api/v1/setup");

        Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return document.RootElement.GetProperty("error").GetProperty("code").GetString();
    }

    private static async Task<JsonElement> ValidationFieldsAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(Token);
        using var document = JsonDocument.Parse(body);
        var error = document.RootElement.GetProperty("error");
        Assert.Equal("validation_failed", error.GetProperty("code").GetString());
        return error.GetProperty("details").GetProperty("fields").Clone();
    }

    private static async Task<List<string?>> UserNamesAsync(StockroomApiFactory factory)
    {
        await using var db = TestDatabase.CreateContext(factory.Settings[StockroomOptions.DatabaseUrlKey]!);
        return await db.Users.Select(u => u.UserName).ToListAsync(Token);
    }
}
