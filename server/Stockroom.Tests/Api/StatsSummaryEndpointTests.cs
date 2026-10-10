using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Stockroom.Core.Settings;
using Stockroom.Core.Users;
using Stockroom.Tests.Infrastructure;
using static Stockroom.Tests.Api.TestAuth;

namespace Stockroom.Tests.Api;

/// <summary>
/// <c>GET /api/v1/stats/summary</c>. The figures cover the whole database, so each test gets one of its own, and
/// stock is seeded through the API so it goes through the ledger.
/// </summary>
public sealed class StatsSummaryEndpointTests(PostgresFixture postgres)
{
    private static readonly Uri SummaryUri = new("/api/v1/stats/summary", UriKind.Relative);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnEmptyStockroomHasNothing()
    {
        await using var app = await StockroomApiFactory.CreateAsync(postgres);
        using var client = await CreateClientAsAsync(app, Roles.Staff);

        var summary = await GetSummaryAsync(client);

        Assert.Equal(
            ["total_products", "total_units", "low_stock_count", "out_of_stock_count", "recent_movements"],
            summary.EnumerateObject().Select(p => p.Name));
        Assert.Equal((0, 0L, 0, 0), Figures(summary));
        Assert.Empty(summary.GetProperty("recent_movements").EnumerateArray());
    }

    [Fact]
    public async Task FiguresCoverActiveProductsAndTheNewestMovements()
    {
        await using var app = await StockroomApiFactory.CreateAsync(postgres);
        var admin = await CreateUserAsync(app, Roles.Admin, "Ada Admin");
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await LoginAsync(app, admin.UserName!)).AccessToken);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISettingsStore>().SetAsync(StockroomSettings.AllowNegativeStock, true, Token);
        }

        var plenty = await CreateAsync(client, minStock: 5, quantity: 10);    // 10 + 6 received below
        await CreateAsync(client, minStock: 5, quantity: 5);                   // low
        await CreateAsync(client, minStock: null, quantity: 0);                // out, never low without a minimum
        var emptied = await CreateAsync(client, minStock: 3, quantity: 2);     // low and out once issued
        await MoveAsync(client, emptied, "issue", 2);
        var negative = await CreateAsync(client, minStock: null, quantity: 1); // out at -3, adding no units
        await MoveAsync(client, negative, "issue", 4);
        var archived = await CreateAsync(client, minStock: 10, quantity: 7);   // left out of every figure
        using (var response = await client.PostAsync(new Uri($"/api/v1/products/{archived}/archive", UriKind.Relative), content: null, Token))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var received = new List<Guid>();
        for (var i = 0; i < 6; i++)
        {
            received.Add(await MoveAsync(client, plenty, "receive", 1));
        }

        var summary = await GetSummaryAsync(client);
        var recent = summary.GetProperty("recent_movements").EnumerateArray().ToList();

        Assert.Equal((5, 21L, 2, 3), Figures(summary));

        // 13 movements in all: 6 initial, 2 issues, and the 6 receives, which are the newest.
        Assert.Equal(10, recent.Count);
        Assert.Equal(Enumerable.Reverse(received), recent.Take(6).Select(m => m.GetProperty("id").GetGuid()));
        Assert.Equal(
            recent.Select(m => m.GetProperty("created_at").GetDateTimeOffset()).OrderDescending(),
            recent.Select(m => m.GetProperty("created_at").GetDateTimeOffset()));
        Assert.Equal(
            ["id", "product_id", "product_sku", "product_name", "type", "delta", "quantity_after", "reason", "note", "reference", "voids_movement_id", "actor_id", "actor_name", "created_at"],
            recent[0].EnumerateObject().Select(p => p.Name));

        // A STAFF caller, who cannot list users, still sees who moved what.
        using var staff = await CreateClientAsAsync(app, Roles.Staff);
        var newest = (await GetSummaryAsync(staff)).GetProperty("recent_movements")[0];
        var product = await GetProductAsync(staff, plenty);
        Assert.Equal(
            (product.GetProperty("sku").GetString(), product.GetProperty("name").GetString(), "Ada Admin"),
            (newest.GetProperty("product_sku").GetString(), newest.GetProperty("product_name").GetString(), newest.GetProperty("actor_name").GetString()));
    }

    [Fact]
    public async Task AnonymousCallersCannotReadStats()
    {
        await using var app = await StockroomApiFactory.CreateAsync(postgres);
        using var client = app.CreateClient();

        using var response = await client.GetAsync(SummaryUri, Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static (int Products, long Units, int LowStock, int OutOfStock) Figures(JsonElement summary) =>
        (summary.GetProperty("total_products").GetInt32(),
            summary.GetProperty("total_units").GetInt64(),
            summary.GetProperty("low_stock_count").GetInt32(),
            summary.GetProperty("out_of_stock_count").GetInt32());

    private static async Task<Guid> CreateAsync(HttpClient client, int? minStock, int quantity)
    {
        using var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/products", UriKind.Relative),
            new { name = $"Product {Guid.NewGuid():N}", min_stock = minStock, initial_quantity = quantity },
            Token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadAsync(response)).GetProperty("id").GetGuid();
    }

    /// <summary>Records a movement and returns its public ID.</summary>
    private static async Task<Guid> MoveAsync(HttpClient client, Guid product, string type, int quantity)
    {
        using var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/stock/movements", UriKind.Relative),
            new { product_id = product, type, quantity },
            Token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> GetProductAsync(HttpClient client, Guid product)
    {
        using var response = await client.GetAsync(new Uri($"/api/v1/products/{product}", UriKind.Relative), Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync(response);
    }

    private static async Task<JsonElement> GetSummaryAsync(HttpClient client)
    {
        using var response = await client.GetAsync(SummaryUri, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync(response);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return document.RootElement.Clone();
    }
}
