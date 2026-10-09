using System.Net;
using System.Text.Json;
using Stockroom.Api.Configuration;
using Stockroom.Core.Products;
using Stockroom.Core.Users;
using Stockroom.Tests.Data;
using Stockroom.Tests.Infrastructure;
using static Stockroom.Tests.Api.TestAuth;

namespace Stockroom.Tests.Api;

/// <summary>
/// The <c>q</c> search of <c>GET /api/v1/products</c>. The catalogue is shared, so every product name and SKU
/// carries a token unique to its test, and searches include it where they would otherwise match other tests' products.
/// </summary>
public sealed class ProductSearchEndpointTests(StockroomApiFactory factory) : IClassFixture<StockroomApiFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string DatabaseUrl => factory.Settings[StockroomOptions.DatabaseUrlKey]!;

    [Fact]
    public async Task PartOfTheNameMatchesIgnoringCase()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var token = NewToken();
        var bolt = await SaveAsync($"{token} Hex Bolt M8");
        var nut = await SaveAsync($"{token} HEX NUT");
        await SaveAsync($"{token} Wood screw");

        Assert.Equal(new[] { bolt.PublicId, nut.PublicId }.Order(), (await SearchAsync(client, $"{token} hex")).Order());
        Assert.Equal([bolt.PublicId], await SearchAsync(client, $"{token} hEX b"));
        Assert.Equal([bolt.PublicId], await SearchAsync(client, "x BOLT m8"));
    }

    [Fact]
    public async Task PartOfTheSkuMatchesIgnoringCase()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var token = NewToken();
        var product = await SaveAsync("Anchor", sku: $"BLT-{token}-0042");

        Assert.Equal([product.PublicId], await SearchAsync(client, $"blt-{token.ToUpperInvariant()}-00"));
    }

    [Fact]
    public async Task PartOfAnyBarcodeMatchesAndListsTheProductOnce()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var digits = Random.Shared.NextInt64(1_000_000_000, 9_999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var product = await SaveAsync("Bolt", barcodes: [$"40{digits}1", $"QR-{digits}-box"]);

        Assert.Equal([product.PublicId], await SearchAsync(client, digits));
        Assert.Equal([product.PublicId], await SearchAsync(client, $"qr-{digits}-BOX"));
    }

    [Fact]
    public async Task WildcardCharactersAreMatchedLiterally()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var token = NewToken();
        var percent = await SaveAsync($"{token} 100% cotton");
        var underscore = await SaveAsync($"{token} rag_bag");
        await SaveAsync($"{token} 1000 cotton");
        await SaveAsync($"{token} ragsbag");

        // Unescaped, % and _ would match "1000 cotton" and "ragsbag", and a trailing \ would be an invalid pattern.
        Assert.Equal([percent.PublicId], await SearchAsync(client, $"{token} 100%"));
        Assert.Equal([underscore.PublicId], await SearchAsync(client, $"{token} rag_"));
        Assert.Empty(await SearchAsync(client, $"{token} 100\\"));
    }

    [Fact]
    public async Task ResultsPageWithTheSearch()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var token = NewToken();
        var products = new List<Product>();
        foreach (var name in new[] { "Anchor", "Bolt", "Clamp", "Drill", "Epoxy" })
        {
            products.Add(await SaveAsync($"{name} {token}"));
        }

        await SaveAsync("Unrelated");

        var first = await GetPageAsync(client, $"?q={token}&limit=3");
        var second = await GetPageAsync(client, $"?q={token}&limit=3&cursor={first.NextCursor}");

        Assert.Equal(products.Select(p => p.PublicId), first.Ids.Concat(second.Ids));
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task SurroundingSpaceIsIgnoredAndBlankMatchesEverything()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var token = NewToken();
        var product = await SaveAsync($"Funnel {token}");

        Assert.Equal([product.PublicId], await SearchAsync(client, $"  {token} "));
        Assert.Equal(await AllIdsAsync(client, ""), await AllIdsAsync(client, "&q=%20%20"));
    }

    [Fact]
    public async Task AnOverlongSearchIsRejected()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);

        using var response = await client.GetAsync(new Uri($"/api/v1/products?q={new string('x', 201)}", UriKind.Relative), Token);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        var error = document.RootElement.GetProperty("error");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", error.GetProperty("code").GetString());
        Assert.Equal(["q"], error.GetProperty("details").GetProperty("fields").EnumerateObject().Select(p => p.Name));
    }

    private static string NewToken() => Guid.NewGuid().ToString("N")[..12];

    private async Task<Product> SaveAsync(string name, string? sku = null, string[]? barcodes = null)
    {
        var product = TestProducts.New(sku ?? $"P-{Guid.NewGuid():N}");
        product.Name = name;
        await TestDatabase.AddAsync(DatabaseUrl, [product, .. (barcodes ?? []).Select(b => TestProducts.Barcode(product.Id, b))]);
        return product;
    }

    private static async Task<List<Guid>> SearchAsync(HttpClient client, string q)
    {
        var page = await GetPageAsync(client, $"?q={Uri.EscapeDataString(q)}&limit=100");
        Assert.Null(page.NextCursor);
        return page.Ids;
    }

    private static async Task<List<Guid>> AllIdsAsync(HttpClient client, string query)
    {
        var ids = new List<Guid>();
        string? cursor = null;
        do
        {
            var page = await GetPageAsync(client, "?limit=100" + query + (cursor is null ? "" : $"&cursor={cursor}"));
            ids.AddRange(page.Ids);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        return ids;
    }

    private static async Task<Page> GetPageAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync(new Uri("/api/v1/products" + query, UriKind.Relative), Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        var root = document.RootElement;
        return new Page(
            root.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList(),
            root.GetProperty("next_cursor").GetString());
    }

    private sealed record Page(List<Guid> Ids, string? NextCursor);
}
