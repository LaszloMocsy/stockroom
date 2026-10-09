using System.Net;
using System.Text.Json;
using Stockroom.Api.Configuration;
using Stockroom.Core.Products;
using Stockroom.Core.Users;
using Stockroom.Tests.Infrastructure;
using static Stockroom.Tests.Api.TestAuth;

namespace Stockroom.Tests.Api;

/// <summary>
/// <c>GET /api/v1/products</c>. Each test lists the whole catalogue, so each gets a database of its own;
/// names and SKUs are plain ASCII of one case, so every collation sorts them alike.
/// </summary>
public sealed class ProductListEndpointTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PagesFollowEachOtherInNameOrderUntilTheCursorIsNull()
    {
        await using var app = await StockroomApiFactory.CreateAsync(postgres);
        using var client = await CreateClientAsAsync(app, Roles.Staff);
        string[] names = ["Gasket", "Anchor", "Drill", "Bolt", "Funnel", "Clamp", "Epoxy"];
        var products = await SaveAsync(app, names.Select((name, i) => New(name, $"SKU-{i}", T0.AddMinutes(i))).ToArray());
        var byName = products.OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => p.PublicId).ToList();

        var first = await GetPageAsync(client, "?limit=3");
        var second = await GetPageAsync(client, $"?limit=3&cursor={first.NextCursor}");
        var third = await GetPageAsync(client, $"?limit=3&cursor={second.NextCursor}");

        Assert.Equal(byName[..3], first.Ids);
        Assert.Equal(byName[3..6], second.Ids);
        Assert.Equal(byName[6..], third.Ids);
        Assert.NotNull(first.NextCursor);
        Assert.NotNull(second.NextCursor);
        Assert.Null(third.NextCursor);
        Assert.Equal(
            ["id", "sku", "name", "description", "barcodes", "min_stock", "quantity", "archived_at", "created_at", "updated_at"],
            first.Items[0].EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task ProductsWithTheSameNameAreEachListedOnce()
    {
        await using var app = await StockroomApiFactory.CreateAsync(postgres);
        using var client = await CreateClientAsAsync(app, Roles.Staff);
        var products = await SaveAsync(app, Enumerable.Range(0, 5).Select(i => New("Same", $"SKU-{i}", T0)).ToArray());

        var listed = await GetAllAsync(client, "sort=name", limit: 2);

        Assert.Equal(products.Select(p => p.PublicId).Order(), listed.Order());
    }

    [Theory]
    [InlineData("sku")]
    [InlineData("-sku")]
    [InlineData("created_at")]
    [InlineData("-created_at")]
    [InlineData("-name")]
    public async Task EverySortPagesInItsOrder(string sort)
    {
        await using var app = await StockroomApiFactory.CreateAsync(postgres);
        using var client = await CreateClientAsAsync(app, Roles.Staff);
        // Name, SKU, and creation time each put the products in a different order.
        var products = await SaveAsync(
            app,
            New("Clamp", "SKU-A", T0.AddMinutes(2)),
            New("Anchor", "SKU-D", T0.AddMinutes(4)),
            New("Epoxy", "SKU-B", T0.AddMinutes(1)),
            New("Bolt", "SKU-E", T0.AddMinutes(3)),
            New("Drill", "SKU-C", T0));
        var expected = (sort.TrimStart('-') switch
            {
                "sku" => products.OrderBy(p => p.Sku, StringComparer.Ordinal),
                "name" => products.OrderBy(p => p.Name, StringComparer.Ordinal),
                _ => products.OrderBy(p => p.CreatedAt),
            })
            .Select(p => p.PublicId)
            .ToList();
        if (sort.StartsWith('-'))
        {
            expected.Reverse();
        }

        Assert.Equal(expected, await GetAllAsync(client, $"sort={sort}", limit: 2));
    }

    [Fact]
    public async Task TheDefaultPageHoldsFiftyProducts()
    {
        await using var app = await StockroomApiFactory.CreateAsync(postgres);
        using var client = await CreateClientAsAsync(app, Roles.Staff);
        await SaveAsync(app, Enumerable.Range(0, 51).Select(i => New($"Product {i:D2}", $"SKU-{i:D2}", T0)).ToArray());

        var first = await GetPageAsync(client, "");
        var second = await GetPageAsync(client, $"?cursor={first.NextCursor}");

        Assert.Equal(50, first.Ids.Count);
        Assert.Single(second.Ids);
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task ACursorFromAnotherSortIsRejected()
    {
        await using var app = await StockroomApiFactory.CreateAsync(postgres);
        using var client = await CreateClientAsAsync(app, Roles.Staff);
        await SaveAsync(app, New("Anchor", "SKU-1", T0), New("Bolt", "SKU-2", T0));
        var first = await GetPageAsync(client, "?limit=1");

        using var response = await client.GetAsync(new Uri($"/api/v1/products?sort=sku&limit=1&cursor={first.NextCursor}", UriKind.Relative), Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["cursor"], await InvalidFieldsAsync(response));
    }

    [Theory]
    [InlineData("?sort=price", "sort")]
    [InlineData("?sort=--name", "sort")]
    [InlineData("?sort=Name", "sort")]
    [InlineData("?limit=0", "limit")]
    [InlineData("?limit=101", "limit")]
    [InlineData("?cursor=not-a-cursor", "cursor")]
    [InlineData("?sort=created_at&cursor=MDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAuY3JlYXRlZF9hdC54", "cursor")]
    public async Task InvalidParametersAreRejectedWithTheField(string query, string field)
    {
        await using var app = await StockroomApiFactory.CreateAsync(postgres);
        using var client = await CreateClientAsAsync(app, Roles.Staff);

        using var response = await client.GetAsync(new Uri("/api/v1/products" + query, UriKind.Relative), Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([field], await InvalidFieldsAsync(response));
    }

    [Fact]
    public async Task AnonymousCallersCannotListProducts()
    {
        await using var app = await StockroomApiFactory.CreateAsync(postgres);
        using var client = app.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/products", UriKind.Relative), Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static Product New(string name, string sku, DateTimeOffset createdAt) => new()
    {
        Id = TestDatabase.Ids.NewInternalId(),
        PublicId = TestDatabase.Ids.NewPublicId(),
        Sku = sku,
        Name = name,
        CreatedAt = createdAt,
        UpdatedAt = createdAt,
    };

    private static async Task<Product[]> SaveAsync(StockroomApiFactory app, params Product[] products)
    {
        await TestDatabase.AddAsync(app.Settings[StockroomOptions.DatabaseUrlKey]!, products);
        return products;
    }

    /// <summary>Every product, following cursors from the first page to the last.</summary>
    private static async Task<List<Guid>> GetAllAsync(HttpClient client, string query, int limit)
    {
        var ids = new List<Guid>();
        string? cursor = null;
        do
        {
            var page = await GetPageAsync(client, $"?{query}&limit={limit}" + (cursor is null ? "" : $"&cursor={cursor}"));
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
        Assert.Equal(["items", "next_cursor"], root.EnumerateObject().Select(p => p.Name));
        return new Page(
            root.GetProperty("items").EnumerateArray().Select(i => i.Clone()).ToList(),
            root.GetProperty("next_cursor").GetString());
    }

    private static async Task<List<string>> InvalidFieldsAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        var error = document.RootElement.GetProperty("error");
        Assert.Equal("validation_failed", error.GetProperty("code").GetString());
        return error.GetProperty("details").GetProperty("fields").EnumerateObject().Select(p => p.Name).ToList();
    }

    private sealed record Page(List<JsonElement> Items, string? NextCursor)
    {
        public List<Guid> Ids => Items.Select(i => i.GetProperty("id").GetGuid()).ToList();
    }
}
