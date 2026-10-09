using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Stockroom.Api.Configuration;
using Stockroom.Core.Locations;
using Stockroom.Core.Products;
using Stockroom.Core.Stock;
using Stockroom.Core.Users;
using Stockroom.Data.Products;
using Stockroom.Tests.Data;
using Stockroom.Tests.Infrastructure;
using static Stockroom.Tests.Api.TestAuth;

namespace Stockroom.Tests.Api;

public sealed class ProductEndpointTests(StockroomApiFactory factory) : IClassFixture<StockroomApiFactory>
{
    private static readonly Uri ProductsUri = new("/api/v1/products", UriKind.Relative);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string DatabaseUrl => factory.Settings[StockroomOptions.DatabaseUrlKey]!;

    [Fact]
    public async Task StaffCanCreateAProductWithEveryField()
    {
        var (client, user) = await CreateClientWithUserAsync(Roles.Staff);
        using var _ = client;
        var sku = $"BOLT-{Guid.NewGuid():N}";
        var barcode = NewBarcode();

        using var response = await client.PostAsJsonAsync(
            ProductsUri,
            new { name = "M8 bolt", sku, description = "Zinc plated", min_stock = 20, barcode },
            Token);
        var body = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(
            ["id", "sku", "name", "description", "barcodes", "min_stock", "quantity", "archived_at", "created_at", "updated_at"],
            body.EnumerateObject().Select(p => p.Name));
        Assert.Equal(sku, body.GetProperty("sku").GetString());
        Assert.Equal("M8 bolt", body.GetProperty("name").GetString());
        Assert.Equal("Zinc plated", body.GetProperty("description").GetString());
        Assert.Equal([barcode], body.GetProperty("barcodes").EnumerateArray().Select(b => b.GetString()));
        Assert.Equal(20, body.GetProperty("min_stock").GetInt32());
        Assert.Equal(0, body.GetProperty("quantity").GetInt32());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("archived_at").ValueKind);

        await using var db = TestDatabase.CreateContext(DatabaseUrl);
        var stored = await db.Products.Include(p => p.Barcodes).SingleAsync(p => p.Sku == sku, Token);
        Assert.Equal(stored.PublicId, body.GetProperty("id").GetGuid());
        Assert.Equal(user.Id, stored.CreatedBy);
        Assert.Equal(stored.CreatedAt, stored.UpdatedAt);
        Assert.Equal(barcode, Assert.Single(stored.Barcodes).Barcode);
        Assert.False(await db.StockMovements.AnyAsync(m => m.ProductId == stored.Id, Token));
    }

    [Fact]
    public async Task OnlyTheNameIsRequired()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);

        using var response = await client.PostAsJsonAsync(ProductsUri, new { name = "Cable ties" }, Token);
        var body = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("description").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("min_stock").ValueKind);
        Assert.Empty(body.GetProperty("barcodes").EnumerateArray());
        Assert.Equal(0, body.GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task ASkuIsGeneratedWhenOmitted()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);

        using var first = await client.PostAsJsonAsync(ProductsUri, new { name = "First" }, Token);
        using var second = await client.PostAsJsonAsync(ProductsUri, new { name = "Second" }, Token);
        var firstSku = (await ReadAsync(first)).GetProperty("sku").GetString()!;
        var secondSku = (await ReadAsync(second)).GetProperty("sku").GetString()!;

        Assert.Matches(@"^SR-\d{6,}$", firstSku);
        Assert.Matches(@"^SR-\d{6,}$", secondSku);
        Assert.NotEqual(firstSku, secondSku);
    }

    [Fact]
    public async Task AGeneratedSkuSkipsOneTypedInByHand()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        string current;
        await using (var db = TestDatabase.CreateContext(DatabaseUrl))
        {
            current = await new SkuGenerator(db).NextAsync(Token);
        }

        var number = long.Parse(current[Sku.Prefix.Length..], System.Globalization.CultureInfo.InvariantCulture);
        await TestDatabase.AddAsync(DatabaseUrl, TestProducts.New(Sku.FromNumber(number + 1)));

        using var response = await client.PostAsJsonAsync(ProductsUri, new { name = "After a typed SKU" }, Token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(Sku.FromNumber(number + 2), (await ReadAsync(response)).GetProperty("sku").GetString());
    }

    [Fact]
    public async Task AnInitialQuantityIsRecordedAsAnInitialMovement()
    {
        var (client, user) = await CreateClientWithUserAsync(Roles.Staff);
        using var _ = client;

        using var response = await client.PostAsJsonAsync(ProductsUri, new { name = "Washers", initial_quantity = 250 }, Token);
        var body = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(250, body.GetProperty("quantity").GetInt32());

        await using var db = TestDatabase.CreateContext(DatabaseUrl);
        var product = await db.Products.SingleAsync(p => p.PublicId == body.GetProperty("id").GetGuid(), Token);
        var movement = await db.StockMovements.SingleAsync(m => m.ProductId == product.Id, Token);
        Assert.Equal((StockMovementType.Initial, 250, 250, user.Id), (movement.Type, movement.Delta, movement.QuantityAfter, movement.ActorId));
        Assert.Equal(250, await db.StockLevels.Where(l => l.ProductId == product.Id && l.LocationId == Location.MainStorageId).Select(l => l.Quantity).SingleAsync(Token));
    }

    [Fact]
    public async Task AnInitialQuantityOfZeroRecordsNoMovement()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);

        using var response = await client.PostAsJsonAsync(ProductsUri, new { name = "Empty shelf", initial_quantity = 0 }, Token);
        var id = (await ReadAsync(response)).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var db = TestDatabase.CreateContext(DatabaseUrl);
        var product = await db.Products.SingleAsync(p => p.PublicId == id, Token);
        Assert.False(await db.StockMovements.AnyAsync(m => m.ProductId == product.Id, Token));
    }

    [Fact]
    public async Task ADuplicateSkuIsAConflictNamingTheOtherProduct()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var existing = TestProducts.New($"DUP-{Guid.NewGuid():N}");
        await TestDatabase.AddAsync(DatabaseUrl, existing);

        using var response = await client.PostAsJsonAsync(ProductsUri, new { name = "Copy", sku = existing.Sku, initial_quantity = 5 }, Token);
        var error = (await ReadAsync(response)).GetProperty("error");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("sku_taken", error.GetProperty("code").GetString());
        Assert.Equal(existing.Sku, error.GetProperty("details").GetProperty("sku").GetString());
        Assert.Equal(existing.PublicId, error.GetProperty("details").GetProperty("product_id").GetGuid());
        Assert.Equal(0, await ProductCountAsync("Copy"));
    }

    [Fact]
    public async Task ADuplicateBarcodeIsAConflictAndCreatesNothing()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var existing = TestProducts.New($"P-{Guid.NewGuid():N}");
        var barcode = NewBarcode();
        await TestDatabase.AddAsync(DatabaseUrl, existing, TestProducts.Barcode(existing.Id, barcode));
        var name = $"Rescanned {Guid.NewGuid():N}";

        using var response = await client.PostAsJsonAsync(ProductsUri, new { name, barcode, initial_quantity = 5 }, Token);
        var error = (await ReadAsync(response)).GetProperty("error");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("barcode_taken", error.GetProperty("code").GetString());
        Assert.Equal(barcode, error.GetProperty("details").GetProperty("barcode").GetString());
        Assert.Equal(existing.PublicId, error.GetProperty("details").GetProperty("product_id").GetGuid());
        Assert.Equal(0, await ProductCountAsync(name));
    }

    [Theory]
    [InlineData("""{ "sku": "X-1" }""", "name")]
    [InlineData("""{ "name": " " }""", "name")]
    [InlineData("""{ "name": "{long}" }""", "name")]
    [InlineData("""{ "name": "Bolt", "sku": "" }""", "sku")]
    [InlineData("""{ "name": "Bolt", "barcode": " " }""", "barcode")]
    [InlineData("""{ "name": "Bolt", "min_stock": -1 }""", "min_stock")]
    [InlineData("""{ "name": "Bolt", "initial_quantity": -1 }""", "initial_quantity")]
    [InlineData("""{ "name": "Bolt", "initial_quantity": 1000001 }""", "initial_quantity")]
    public async Task InvalidRequestsAreRejectedWithTheField(string json, string field)
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);

        using var response = await PostJsonAsync(client, json.Replace("{long}", new string('x', 201), StringComparison.Ordinal));
        var error = (await ReadAsync(response)).GetProperty("error");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", error.GetProperty("code").GetString());
        Assert.Equal([field], error.GetProperty("details").GetProperty("fields").EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task AFractionalInitialQuantityIsRejected()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);

        using var response = await PostJsonAsync(client, """{ "name": "Rope", "initial_quantity": 2.5 }""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await ProductCountAsync("Rope"));
    }

    [Fact]
    public async Task AnonymousCallersCannotCreateProducts()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(ProductsUri, new { name = "Anonymous" }, Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, await ProductCountAsync("Anonymous"));
    }

    private async Task<(HttpClient Client, User User)> CreateClientWithUserAsync(string role)
    {
        var user = await CreateUserAsync(factory, role);
        var tokens = await LoginAsync(factory, user.UserName!);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return (client, user);
    }

    private static string NewBarcode() => $"40{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}";

    private static async Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await client.PostAsync(ProductsUri, content, Token);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return document.RootElement.Clone();
    }

    private async Task<int> ProductCountAsync(string name)
    {
        await using var db = TestDatabase.CreateContext(DatabaseUrl);
        return await db.Products.CountAsync(p => p.Name == name, Token);
    }
}
