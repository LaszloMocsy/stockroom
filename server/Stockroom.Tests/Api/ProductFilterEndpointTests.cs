using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Stockroom.Api.Configuration;
using Stockroom.Core.Users;
using Stockroom.Tests.Data;
using Stockroom.Tests.Infrastructure;
using static Stockroom.Tests.Api.TestAuth;

namespace Stockroom.Tests.Api;

/// <summary>
/// The <c>low_stock</c> and <c>archived</c> filters of <c>GET /api/v1/products</c>. Each test lists the whole
/// catalogue, so each gets a database of its own. Stock is set up through the API, so it goes through the ledger.
/// </summary>
public sealed class ProductFilterEndpointTests(PostgresFixture postgres)
{
    private static readonly Uri ProductsUri = new("/api/v1/products", UriKind.Relative);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LowStockSplitsProductsAtTheirMinimum()
    {
        await using var app = await StockroomApiFactory.CreateAsync(postgres);
        using var client = await CreateClientAsAsync(app, Roles.Staff);
        var atMinimum = await CreateAsync(client, "At minimum", minStock: 5, quantity: 5);
        var aboveMinimum = await CreateAsync(client, "Above minimum", minStock: 5, quantity: 6);
        var neverStocked = await CreateAsync(client, "Never stocked", minStock: 5, quantity: 0);
        var noMinimum = await CreateAsync(client, "No minimum", minStock: null, quantity: 0);
        var zeroMinimum = await CreateAsync(client, "Zero minimum", minStock: 0, quantity: 0);

        Assert.Equal(new[] { atMinimum, neverStocked, zeroMinimum }.Order(), (await ListAsync(client, "?low_stock=true")).Order());
        Assert.Equal(new[] { aboveMinimum, noMinimum }.Order(), (await ListAsync(client, "?low_stock=false")).Order());
        Assert.Equal(5, (await ListAsync(client, "")).Count);
    }

    [Fact]
    public async Task LowStockFollowsTheCurrentQuantity()
    {
        await using var app = await StockroomApiFactory.CreateAsync(postgres);
        using var client = await CreateClientAsAsync(app, Roles.Staff);
        var product = await CreateAsync(client, "Bolts", minStock: 10, quantity: 12);
        Assert.Empty(await ListAsync(client, "?low_stock=true"));

        using var issued = await client.PostAsJsonAsync(
            new Uri("/api/v1/stock/movements", UriKind.Relative),
            new { product_id = product, type = "issue", quantity = 2 },
            Token);

        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        Assert.Equal([product], await ListAsync(client, "?low_stock=true"));
    }

    [Fact]
    public async Task ArchivedProductsAreListedOnlyWhenAskedFor()
    {
        await using var app = await StockroomApiFactory.CreateAsync(postgres);
        using var client = await CreateClientAsAsync(app, Roles.Staff);
        var active = await CreateAsync(client, "Active", minStock: null, quantity: 0);
        var archived = TestProducts.New("SKU-ARCHIVED");
        archived.ArchivedAt = DateTimeOffset.UtcNow;
        await TestDatabase.AddAsync(app.Settings[StockroomOptions.DatabaseUrlKey]!, archived);

        Assert.Equal([active], await ListAsync(client, ""));
        Assert.Equal([active], await ListAsync(client, "?archived=false"));
        Assert.Equal([archived.PublicId], await ListAsync(client, "?archived=true"));
    }

    [Fact]
    public async Task FiltersCombine()
    {
        await using var app = await StockroomApiFactory.CreateAsync(postgres);
        using var client = await CreateClientAsAsync(app, Roles.Staff);
        var lowBolt = await CreateAsync(client, "Bolt, low", minStock: 5, quantity: 1);
        await CreateAsync(client, "Bolt, plenty", minStock: 5, quantity: 50);
        await CreateAsync(client, "Nut, low", minStock: 5, quantity: 1);
        var archivedLow = TestProducts.New("SKU-ARCHIVED");
        archivedLow.Name = "Bolt, archived";
        archivedLow.MinStock = 5;
        archivedLow.ArchivedAt = DateTimeOffset.UtcNow;
        await TestDatabase.AddAsync(app.Settings[StockroomOptions.DatabaseUrlKey]!, archivedLow);

        Assert.Equal([lowBolt], await ListAsync(client, "?q=bolt&low_stock=true"));
        Assert.Equal([archivedLow.PublicId], await ListAsync(client, "?q=bolt&low_stock=true&archived=true"));
    }

    [Fact]
    public async Task FilteredResultsPage()
    {
        await using var app = await StockroomApiFactory.CreateAsync(postgres);
        using var client = await CreateClientAsAsync(app, Roles.Staff);
        var low = new List<Guid>();
        foreach (var name in new[] { "Anchor", "Bolt", "Clamp" })
        {
            low.Add(await CreateAsync(client, name, minStock: 3, quantity: 1));
            await CreateAsync(client, name + " spare", minStock: 3, quantity: 9);
        }

        var first = await GetPageAsync(client, "?low_stock=true&limit=2");
        var second = await GetPageAsync(client, $"?low_stock=true&limit=2&cursor={first.NextCursor}");

        Assert.Equal(low, first.Ids.Concat(second.Ids));
        Assert.Null(second.NextCursor);
    }

    [Theory]
    [InlineData("?low_stock=yes")]
    [InlineData("?archived=1")]
    public async Task AFilterThatIsNotABooleanIsRejected(string query)
    {
        await using var app = await StockroomApiFactory.CreateAsync(postgres);
        using var client = await CreateClientAsAsync(app, Roles.Staff);

        using var response = await client.GetAsync(new Uri("/api/v1/products" + query, UriKind.Relative), Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("bad_request", await ErrorCodeAsync(response));
    }

    /// <summary>Creates a product through the API and returns its public ID.</summary>
    private static async Task<Guid> CreateAsync(HttpClient client, string name, int? minStock, int quantity)
    {
        using var response = await client.PostAsJsonAsync(ProductsUri, new { name, min_stock = minStock, initial_quantity = quantity }, Token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return document.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<List<Guid>> ListAsync(HttpClient client, string query)
    {
        var page = await GetPageAsync(client, query);
        Assert.Null(page.NextCursor);
        return page.Ids;
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
