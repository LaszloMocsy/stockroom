using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Stockroom.Api.Configuration;
using Stockroom.Core.Users;
using Stockroom.Tests.Infrastructure;
using static Stockroom.Tests.Api.TestAuth;

namespace Stockroom.Tests.Api;

public sealed class ProductUpdateEndpointTests(StockroomApiFactory factory) : IClassFixture<StockroomApiFactory>
{
    private static readonly Uri ProductsUri = new("/api/v1/products", UriKind.Relative);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string DatabaseUrl => factory.Settings[StockroomOptions.DatabaseUrlKey]!;

    [Fact]
    public async Task StaffCanChangeTheNameDescriptionAndMinimum()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var created = await CreateAsync(client);
        var id = created.GetProperty("id").GetGuid();

        using var response = await PatchAsync(client, id, """{ "name": "Hex bolt M10", "description": "Galvanised", "min_stock": 40 }""");
        var body = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(id, body.GetProperty("id").GetGuid());
        Assert.Equal("Hex bolt M10", body.GetProperty("name").GetString());
        Assert.Equal("Galvanised", body.GetProperty("description").GetString());
        Assert.Equal(40, body.GetProperty("min_stock").GetInt32());
        Assert.Equal(created.GetProperty("sku").GetString(), body.GetProperty("sku").GetString());
        Assert.Equal(12, body.GetProperty("quantity").GetInt32());
        Assert.Equal(created.GetProperty("created_at").GetDateTimeOffset(), body.GetProperty("created_at").GetDateTimeOffset());
        Assert.True(body.GetProperty("updated_at").GetDateTimeOffset() > created.GetProperty("updated_at").GetDateTimeOffset());

        await using var db = TestDatabase.CreateContext(DatabaseUrl);
        var stored = await db.Products.SingleAsync(p => p.PublicId == id, Token);
        Assert.Equal(("Hex bolt M10", "Galvanised", 40), (stored.Name, stored.Description, stored.MinStock));
    }

    [Fact]
    public async Task OmittedFieldsStayAsTheyAre()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var id = (await CreateAsync(client)).GetProperty("id").GetGuid();

        using var response = await PatchAsync(client, id, """{ "name": "Renamed" }""");
        var body = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Renamed", body.GetProperty("name").GetString());
        Assert.Equal("Zinc plated", body.GetProperty("description").GetString());
        Assert.Equal(20, body.GetProperty("min_stock").GetInt32());
    }

    [Fact]
    public async Task NullRemovesTheDescriptionAndMinimum()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var id = (await CreateAsync(client)).GetProperty("id").GetGuid();

        using var response = await PatchAsync(client, id, """{ "description": null, "min_stock": null }""");
        var body = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("M8 bolt", body.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("description").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("min_stock").ValueKind);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "name": "M8 bolt", "description": "Zinc plated", "min_stock": 20 }""")]
    public async Task APatchThatChangesNothingKeepsUpdatedAt(string json)
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var created = await CreateAsync(client);

        using var response = await PatchAsync(client, created.GetProperty("id").GetGuid(), json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(created.GetProperty("updated_at").GetDateTimeOffset(), (await ReadAsync(response)).GetProperty("updated_at").GetDateTimeOffset());
    }

    [Theory]
    [InlineData("""{ "sku": "SR-999999" }""")]
    [InlineData("""{ "name": "Renamed", "sku": "SR-999999" }""")]
    public async Task TheSkuCannotBeChanged(string json)
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var created = await CreateAsync(client);
        var id = created.GetProperty("id").GetGuid();

        using var response = await PatchAsync(client, id, json);
        var error = (await ReadAsync(response)).GetProperty("error");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", error.GetProperty("code").GetString());
        Assert.Equal(["sku"], error.GetProperty("details").GetProperty("fields").EnumerateObject().Select(p => p.Name));

        // Nothing else in the request is applied either.
        await using var db = TestDatabase.CreateContext(DatabaseUrl);
        var stored = await db.Products.SingleAsync(p => p.PublicId == id, Token);
        Assert.Equal((created.GetProperty("sku").GetString(), "M8 bolt"), (stored.Sku, stored.Name));
    }

    [Theory]
    [InlineData("""{ "name": null }""", "name")]
    [InlineData("""{ "name": "  " }""", "name")]
    [InlineData("""{ "name": "{long}" }""", "name")]
    [InlineData("""{ "description": "{longer}" }""", "description")]
    [InlineData("""{ "min_stock": -1 }""", "min_stock")]
    [InlineData("""{ "min_stok": 5 }""", "min_stok")]
    [InlineData("""{ "quantity": 5 }""", "quantity")]
    public async Task InvalidChangesAreRejectedWithTheField(string json, string field)
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var created = await CreateAsync(client);

        using var response = await PatchAsync(
            client,
            created.GetProperty("id").GetGuid(),
            json.Replace("{long}", new string('x', 201), StringComparison.Ordinal).Replace("{longer}", new string('x', 2001), StringComparison.Ordinal));
        var error = (await ReadAsync(response)).GetProperty("error");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", error.GetProperty("code").GetString());
        Assert.Equal([field], error.GetProperty("details").GetProperty("fields").EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task AnUnknownProductIsNotFound()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);

        using var response = await PatchAsync(client, Guid.NewGuid(), """{ "name": "Ghost" }""");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("not_found", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task AnonymousCallersCannotChangeProducts()
    {
        using var staff = await CreateClientAsAsync(factory, Roles.Staff);
        var id = (await CreateAsync(staff)).GetProperty("id").GetGuid();
        using var client = factory.CreateClient();

        using var response = await PatchAsync(client, id, """{ "name": "Anonymous" }""");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Creates "M8 bolt" (description "Zinc plated", minimum 20, 12 on hand) and returns the response body.</summary>
    private static async Task<JsonElement> CreateAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            ProductsUri,
            new { name = "M8 bolt", description = "Zinc plated", min_stock = 20, initial_quantity = 12 },
            Token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync(response);
    }

    private static async Task<HttpResponseMessage> PatchAsync(HttpClient client, Guid id, string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await client.PatchAsync(new Uri($"/api/v1/products/{id}", UriKind.Relative), content, Token);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return document.RootElement.Clone();
    }
}
