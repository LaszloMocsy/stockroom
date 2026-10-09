using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Stockroom.Api.Configuration;
using Stockroom.Core.Users;
using Stockroom.Tests.Data;
using Stockroom.Tests.Infrastructure;
using static Stockroom.Tests.Api.TestAuth;

namespace Stockroom.Tests.Api;

/// <summary><c>GET /api/v1/products/lookup</c>.</summary>
public sealed class ProductLookupEndpointTests(StockroomApiFactory factory) : IClassFixture<StockroomApiFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string DatabaseUrl => factory.Settings[StockroomOptions.DatabaseUrlKey]!;

    [Fact]
    public async Task AProductIsFoundByAnyOfItsBarcodes()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var (first, second) = (NewBarcode(), $"https://example.com/p/{Guid.NewGuid():N}?lot=7");
        var id = await CreateAsync(client, first, initialQuantity: 3);
        using var added = await client.PostAsJsonAsync(new Uri($"/api/v1/products/{id}/barcodes", UriKind.Relative), new { barcode = second }, Token);

        var byFirst = await LookUpAsync(client, $"barcode={Uri.EscapeDataString(first)}");
        var bySecond = await LookUpAsync(client, $"barcode={Uri.EscapeDataString(second)}");

        Assert.Equal(HttpStatusCode.OK, byFirst.Status);
        Assert.Equal(id, byFirst.Body.GetProperty("id").GetGuid());
        Assert.Equal(3, byFirst.Body.GetProperty("quantity").GetInt32());
        Assert.Equal([first, second], byFirst.Body.GetProperty("barcodes").EnumerateArray().Select(b => b.GetString()));
        Assert.Equal(id, bySecond.Body.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task AProductIsFoundByItsSku()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var id = await CreateAsync(client, barcode: null);
        var sku = (await GetAsync(client, id)).GetProperty("sku").GetString()!;

        var found = await LookUpAsync(client, $"sku={Uri.EscapeDataString(sku)}");

        Assert.Equal(HttpStatusCode.OK, found.Status);
        Assert.Equal(id, found.Body.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task AnArchivedProductIsFoundAndMarked()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var product = TestProducts.New($"ARCH-{Guid.NewGuid():N}");
        product.ArchivedAt = DateTimeOffset.UtcNow;
        var barcode = NewBarcode();
        await TestDatabase.AddAsync(DatabaseUrl, product, TestProducts.Barcode(product.Id, barcode));

        var byBarcode = await LookUpAsync(client, $"barcode={barcode}");
        var bySku = await LookUpAsync(client, $"sku={product.Sku}");

        Assert.Equal(HttpStatusCode.OK, byBarcode.Status);
        Assert.Equal(product.PublicId, byBarcode.Body.GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.String, byBarcode.Body.GetProperty("archived_at").ValueKind);
        Assert.Equal(product.PublicId, bySku.Body.GetProperty("id").GetGuid());
    }

    [Theory]
    [InlineData("barcode")]
    [InlineData("sku")]
    public async Task NoMatchIsNotFound(string parameter)
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);

        var result = await LookUpAsync(client, $"{parameter}=unknown-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.NotFound, result.Status);
        Assert.Equal("not_found", result.Body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task MatchingIsExact()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var barcode = $"Qr-{Guid.NewGuid():N}";
        await CreateAsync(client, barcode);

        Assert.Equal(HttpStatusCode.NotFound, (await LookUpAsync(client, $"barcode={barcode.ToUpperInvariant()}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await LookUpAsync(client, $"barcode={barcode[..^1]}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await LookUpAsync(client, $"sku={barcode}")).Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("barcode=4006381333931&sku=SR-000001")]
    public async Task ExactlyOneOfBarcodeAndSkuIsRequired(string query)
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);

        var result = await LookUpAsync(client, query);
        var error = result.Body.GetProperty("error");

        Assert.Equal(HttpStatusCode.BadRequest, result.Status);
        Assert.Equal("validation_failed", error.GetProperty("code").GetString());
        Assert.Equal(["barcode", "sku"], error.GetProperty("details").GetProperty("fields").EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task AnonymousCallersCannotLookUpProducts()
    {
        using var client = factory.CreateClient();

        var result = await LookUpAsync(client, "sku=SR-000001");

        Assert.Equal(HttpStatusCode.Unauthorized, result.Status);
    }

    private static string NewBarcode() => $"40{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}";

    private static async Task<Guid> CreateAsync(HttpClient client, string? barcode, int initialQuantity = 0)
    {
        using var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/products", UriKind.Relative),
            new { name = "Scanned", barcode, initial_quantity = initialQuantity },
            Token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> GetAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync(new Uri($"/api/v1/products/{id}", UriKind.Relative), Token);
        return await ReadAsync(response);
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> LookUpAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync(new Uri($"/api/v1/products/lookup?{query}", UriKind.Relative), Token);
        var text = await response.Content.ReadAsStringAsync(Token);
        if (text.Length == 0)
        {
            return (response.StatusCode, default);
        }

        using var document = JsonDocument.Parse(text);
        return (response.StatusCode, document.RootElement.Clone());
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return document.RootElement.Clone();
    }
}
