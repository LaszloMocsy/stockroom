using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Stockroom.Api.Configuration;
using Stockroom.Core.Users;
using Stockroom.Tests.Infrastructure;
using static Stockroom.Tests.Api.TestAuth;

namespace Stockroom.Tests.Api;

/// <summary>
/// <c>POST /api/v1/products/:id/archive</c> and <c>/restore</c>. The catalogue is shared, so each product's name
/// carries a token unique to its test, and lists search for it.
/// </summary>
public sealed class ProductArchiveEndpointTests(StockroomApiFactory factory) : IClassFixture<StockroomApiFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string DatabaseUrl => factory.Settings[StockroomOptions.DatabaseUrlKey]!;

    [Fact]
    public async Task AnArchivedProductIsHiddenFromTheListButKeepsItsStock()
    {
        using var admin = await CreateClientAsAsync(factory, Roles.Admin);
        var (id, token) = await CreateAsync(admin, initialQuantity: 7);

        using var response = await PostAsync(admin, id, "archive");
        var body = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(id, body.GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.String, body.GetProperty("archived_at").ValueKind);
        Assert.Equal(7, body.GetProperty("quantity").GetInt32());
        Assert.Empty(await ListAsync(admin, $"?q={token}"));
        Assert.Equal([id], await ListAsync(admin, $"?q={token}&archived=true"));

        using var fetched = await admin.GetAsync(new Uri($"/api/v1/products/{id}", UriKind.Relative), Token);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
    }

    [Fact]
    public async Task ArchivingAgainKeepsTheFirstArchiveTime()
    {
        using var admin = await CreateClientAsAsync(factory, Roles.Admin);
        var (id, _) = await CreateAsync(admin);
        using var first = await PostAsync(admin, id, "archive");

        using var second = await PostAsync(admin, id, "archive");

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal((await ReadAsync(first)).GetProperty("archived_at").GetDateTimeOffset(), (await ReadAsync(second)).GetProperty("archived_at").GetDateTimeOffset());
    }

    [Fact]
    public async Task ARestoredProductIsListedAgain()
    {
        using var admin = await CreateClientAsAsync(factory, Roles.Admin);
        var (id, token) = await CreateAsync(admin);
        using var archived = await PostAsync(admin, id, "archive");

        using var response = await PostAsync(admin, id, "restore");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await ReadAsync(response)).GetProperty("archived_at").ValueKind);
        Assert.Equal([id], await ListAsync(admin, $"?q={token}"));
        Assert.Empty(await ListAsync(admin, $"?q={token}&archived=true"));
    }

    [Fact]
    public async Task RestoringAnActiveProductChangesNothing()
    {
        using var admin = await CreateClientAsAsync(factory, Roles.Admin);
        var (id, _) = await CreateAsync(admin);

        using var response = await PostAsync(admin, id, "restore");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await ReadAsync(response)).GetProperty("archived_at").ValueKind);
    }

    [Theory]
    [InlineData("archive", false)]
    [InlineData("restore", true)]
    public async Task StaffAreForbidden(string action, bool archivedBefore)
    {
        using var admin = await CreateClientAsAsync(factory, Roles.Admin);
        using var staff = await CreateClientAsAsync(factory, Roles.Staff);
        var (id, _) = await CreateAsync(admin);
        if (archivedBefore)
        {
            using var archived = await PostAsync(admin, id, "archive");
        }

        using var response = await PostAsync(staff, id, action);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("forbidden", await ErrorCodeAsync(response));
        await using var db = TestDatabase.CreateContext(DatabaseUrl);
        Assert.Equal(archivedBefore, await db.Products.Where(p => p.PublicId == id).Select(p => p.ArchivedAt != null).SingleAsync(Token));
    }

    [Theory]
    [InlineData("archive")]
    [InlineData("restore")]
    public async Task AnUnknownProductIsNotFound(string action)
    {
        using var admin = await CreateClientAsAsync(factory, Roles.Admin);

        using var response = await PostAsync(admin, Guid.NewGuid(), action);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("not_found", await ErrorCodeAsync(response));
    }

    [Theory]
    [InlineData("archive")]
    [InlineData("restore")]
    public async Task AnonymousCallersAreUnauthorized(string action)
    {
        using var admin = await CreateClientAsAsync(factory, Roles.Admin);
        var (id, _) = await CreateAsync(admin);
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, id, action);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Creates a product named after a new token; returns its public ID and the token.</summary>
    private static async Task<(Guid Id, string Token)> CreateAsync(HttpClient client, int initialQuantity = 0)
    {
        var token = Guid.NewGuid().ToString("N");
        using var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/products", UriKind.Relative),
            new { name = $"Product {token}", initial_quantity = initialQuantity },
            Token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return ((await ReadAsync(response)).GetProperty("id").GetGuid(), token);
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, Guid id, string action) =>
        client.PostAsync(new Uri($"/api/v1/products/{id}/{action}", UriKind.Relative), content: null, Token);

    private static async Task<List<Guid>> ListAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync(new Uri("/api/v1/products" + query, UriKind.Relative), Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadAsync(response)).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return document.RootElement.Clone();
    }
}
