using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Stockroom.Api.Configuration;
using Stockroom.Core.Users;
using Stockroom.Tests.Data;
using Stockroom.Tests.Infrastructure;
using static Stockroom.Tests.Api.TestAuth;

namespace Stockroom.Tests.Api;

public sealed class ProductGetEndpointTests(StockroomApiFactory factory) : IClassFixture<StockroomApiFactory>
{
    private static readonly Uri ProductsUri = new("/api/v1/products", UriKind.Relative);

    private static readonly Uri MovementsUri = new("/api/v1/stock/movements", UriKind.Relative);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string DatabaseUrl => factory.Settings[StockroomOptions.DatabaseUrlKey]!;

    [Fact]
    public async Task AProductIsReturnedWithItsCurrentQuantity()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        using var created = await client.PostAsJsonAsync(
            ProductsUri,
            new { name = "Hex nut", sku = $"NUT-{Guid.NewGuid():N}", description = "M6", min_stock = 5, barcode = $"B-{Guid.NewGuid():N}", initial_quantity = 10 },
            Token);
        var id = (await ReadAsync(created)).GetProperty("id").GetGuid();
        using var issued = await client.PostAsJsonAsync(MovementsUri, new { product_id = id, type = "issue", quantity = 3 }, Token);
        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);

        using var response = await client.GetAsync(ProductUri(id), Token);
        var body = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            ["id", "sku", "name", "description", "barcodes", "min_stock", "quantity", "archived_at", "created_at", "updated_at"],
            body.EnumerateObject().Select(p => p.Name));
        Assert.Equal(id, body.GetProperty("id").GetGuid());
        Assert.Equal("Hex nut", body.GetProperty("name").GetString());
        Assert.Equal(5, body.GetProperty("min_stock").GetInt32());
        Assert.Equal(7, body.GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task TheCreatedProductIsAtItsLocation()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        using var created = await client.PostAsJsonAsync(ProductsUri, new { name = "Located", initial_quantity = 4 }, Token);

        using var response = await client.GetAsync(created.Headers.Location, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(await created.Content.ReadAsStringAsync(Token), await response.Content.ReadAsStringAsync(Token));
    }

    [Fact]
    public async Task TheInternalIdIsNeverInTheResponse()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var product = TestProducts.New($"P-{Guid.NewGuid():N}");
        await TestDatabase.AddAsync(DatabaseUrl, product, TestProducts.Barcode(product.Id, $"B-{Guid.NewGuid():N}"));

        using var response = await client.GetAsync(ProductUri(product.PublicId), Token);
        var json = await response.Content.ReadAsStringAsync(Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(product.Id.ToString(), json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, (await ReadAsync(response)).GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task AnArchivedProductIsStillReturned()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var product = TestProducts.New($"P-{Guid.NewGuid():N}");
        product.ArchivedAt = DateTimeOffset.UtcNow;
        await TestDatabase.AddAsync(DatabaseUrl, product);

        using var response = await client.GetAsync(ProductUri(product.PublicId), Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.String, (await ReadAsync(response)).GetProperty("archived_at").ValueKind);
    }

    [Fact]
    public async Task AnUnknownIdIsNotFound()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);

        using var response = await client.GetAsync(ProductUri(Guid.NewGuid()), Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("not_found", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task TheInternalIdIsNotAccepted()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var product = TestProducts.New($"P-{Guid.NewGuid():N}");
        await TestDatabase.AddAsync(DatabaseUrl, product);

        using var response = await client.GetAsync(ProductUri(product.Id), Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousCallersCannotReadProducts()
    {
        using var client = factory.CreateClient();
        var product = TestProducts.New($"P-{Guid.NewGuid():N}");
        await TestDatabase.AddAsync(DatabaseUrl, product);

        using var response = await client.GetAsync(ProductUri(product.PublicId), Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static Uri ProductUri(Guid id) => new($"/api/v1/products/{id}", UriKind.Relative);

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return document.RootElement.Clone();
    }
}
