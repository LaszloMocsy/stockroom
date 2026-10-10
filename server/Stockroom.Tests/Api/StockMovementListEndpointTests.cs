using System.Net;
using System.Text.Json;
using Stockroom.Api.Configuration;
using Stockroom.Core.Locations;
using Stockroom.Core.Products;
using Stockroom.Core.Stock;
using Stockroom.Core.Users;
using Stockroom.Tests.Data;
using Stockroom.Tests.Infrastructure;
using static Stockroom.Tests.Api.TestAuth;

namespace Stockroom.Tests.Api;

/// <summary>
/// <c>GET /api/v1/stock/movements</c>. The movements are saved directly, so each test controls their times;
/// most tests filter by a product of their own, so they share one database without seeing each other's.
/// </summary>
public sealed class StockMovementListEndpointTests(StockroomApiFactory factory, PostgresFixture postgres) : IClassFixture<StockroomApiFactory>
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MovementsAreListedNewestFirstWithPublicIdsAndNames()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var actor = await CreateUserAsync(factory, Roles.Staff, "Anna Staff");
        var product = TestProducts.New($"P-{Guid.NewGuid():N}");
        var received = Movement(product, actor, StockMovementType.Receive, T0, delta: 10);
        var issued = Movement(product, actor, StockMovementType.Issue, T0.AddMinutes(1), delta: -3);
        var voided = Movement(product, actor, StockMovementType.Void, T0.AddMinutes(2), delta: 3, voids: issued);
        await TestDatabase.AddAsync(factory.Settings[StockroomOptions.DatabaseUrlKey]!, product, received, issued, voided);

        var page = await GetPageAsync(client, $"?product={product.PublicId}");

        Assert.Equal([voided.PublicId, issued.PublicId, received.PublicId], page.Ids);
        Assert.Null(page.NextCursor);
        var first = page.Items[0];
        Assert.Equal(
            ["id", "product_id", "product_sku", "product_name", "type", "delta", "quantity_after", "reason", "note", "reference", "voids_movement_id", "actor_id", "actor_name", "created_at"],
            first.EnumerateObject().Select(p => p.Name));
        Assert.Equal(product.PublicId, first.GetProperty("product_id").GetGuid());
        Assert.Equal(product.Sku, first.GetProperty("product_sku").GetString());
        Assert.Equal(product.Name, first.GetProperty("product_name").GetString());
        Assert.Equal("void", first.GetProperty("type").GetString());
        Assert.Equal(issued.PublicId, first.GetProperty("voids_movement_id").GetGuid());
        Assert.Equal(actor.PublicId, first.GetProperty("actor_id").GetGuid());
        Assert.Equal("Anna Staff", first.GetProperty("actor_name").GetString());
        Assert.Equal(T0.AddMinutes(2), first.GetProperty("created_at").GetDateTimeOffset());
    }

    [Fact]
    public async Task PagesFollowEachOtherUntilTheCursorIsNull()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var (product, movements) = await SaveMovementsAsync(7, i => T0.AddMinutes(i));
        var newestFirst = movements.OrderByDescending(m => m.CreatedAt).Select(m => m.PublicId).ToList();

        var first = await GetPageAsync(client, $"?product={product.PublicId}&limit=3");
        var second = await GetPageAsync(client, $"?product={product.PublicId}&limit=3&cursor={first.NextCursor}");
        var third = await GetPageAsync(client, $"?product={product.PublicId}&limit=3&cursor={second.NextCursor}");

        Assert.Equal(newestFirst[..3], first.Ids);
        Assert.Equal(newestFirst[3..6], second.Ids);
        Assert.Equal(newestFirst[6..], third.Ids);
        Assert.NotNull(first.NextCursor);
        Assert.NotNull(second.NextCursor);
        Assert.Null(third.NextCursor);
    }

    [Fact]
    public async Task AFullLastPageHasNoNextCursor()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var (product, _) = await SaveMovementsAsync(4, i => T0.AddMinutes(i));

        var first = await GetPageAsync(client, $"?product={product.PublicId}&limit=2");
        var second = await GetPageAsync(client, $"?product={product.PublicId}&limit=2&cursor={first.NextCursor}");

        Assert.Equal(2, second.Ids.Count);
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task MovementsWithTheSameTimeAreNeitherRepeatedNorSkipped()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var (product, movements) = await SaveMovementsAsync(5, _ => T0);

        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            var page = await GetPageAsync(client, $"?product={product.PublicId}&limit=2" + (cursor is null ? "" : $"&cursor={cursor}"));
            seen.AddRange(page.Ids);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(movements.Select(m => m.PublicId).Order(), seen.Order());
        Assert.Equal(5, seen.Distinct().Count());
    }

    [Fact]
    public async Task APageHoldsFiftyMovementsByDefault()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var (product, _) = await SaveMovementsAsync(51, i => T0.AddSeconds(i));

        var page = await GetPageAsync(client, $"?product={product.PublicId}");

        Assert.Equal(50, page.Ids.Count);
        Assert.NotNull(page.NextCursor);
    }

    [Fact]
    public async Task WithoutFiltersEveryMovementIsListed()
    {
        // Its own database, so no other test's movements show up.
        await using var app = await StockroomApiFactory.CreateAsync(postgres);
        using var client = await CreateClientAsAsync(app, Roles.Staff);
        var actor = await CreateUserAsync(app, Roles.Staff);
        var first = TestProducts.New("SR-000001");
        var second = TestProducts.New("SR-000002");
        var movements = new[]
        {
            Movement(first, actor, StockMovementType.Receive, T0),
            Movement(second, actor, StockMovementType.Receive, T0.AddMinutes(1)),
        };
        await TestDatabase.AddAsync(app.Settings[StockroomOptions.DatabaseUrlKey]!, [first, second, .. movements]);

        var page = await GetPageAsync(client, "");

        Assert.Equal([movements[1].PublicId, movements[0].PublicId], page.Ids);
    }

    [Fact]
    public async Task MovementsAreFilteredByProduct()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var actor = await CreateUserAsync(factory, Roles.Staff);
        var wanted = TestProducts.New($"P-{Guid.NewGuid():N}");
        var other = TestProducts.New($"P-{Guid.NewGuid():N}");
        var movement = Movement(wanted, actor, StockMovementType.Receive, T0);
        await TestDatabase.AddAsync(DatabaseUrl, wanted, other, movement, Movement(other, actor, StockMovementType.Receive, T0));

        var page = await GetPageAsync(client, $"?product={wanted.PublicId}");

        Assert.Equal([movement.PublicId], page.Ids);
    }

    [Fact]
    public async Task MovementsAreFilteredByType()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var actor = await CreateUserAsync(factory, Roles.Staff);
        var product = TestProducts.New($"P-{Guid.NewGuid():N}");
        var issue = Movement(product, actor, StockMovementType.Issue, T0.AddMinutes(1), delta: -1);
        await TestDatabase.AddAsync(DatabaseUrl, product, Movement(product, actor, StockMovementType.Receive, T0), issue);

        var page = await GetPageAsync(client, $"?product={product.PublicId}&type=issue");

        Assert.Equal([issue.PublicId], page.Ids);
    }

    [Fact]
    public async Task MovementsAreFilteredByActor()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var anna = await CreateUserAsync(factory, Roles.Staff);
        var bela = await CreateUserAsync(factory, Roles.Admin);
        var product = TestProducts.New($"P-{Guid.NewGuid():N}");
        var byBela = Movement(product, bela, StockMovementType.Receive, T0.AddMinutes(1));
        await TestDatabase.AddAsync(DatabaseUrl, product, Movement(product, anna, StockMovementType.Receive, T0), byBela);

        var page = await GetPageAsync(client, $"?product={product.PublicId}&actor={bela.PublicId}");

        Assert.Equal([byBela.PublicId], page.Ids);
    }

    [Fact]
    public async Task MovementsAreFilteredByTimeFromInclusiveToExclusive()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var (product, movements) = await SaveMovementsAsync(5, i => T0.AddHours(i));

        // 10:00+02:00 is 08:00 UTC, so this is 09:00 to 11:00 UTC: the movements at 09:00 and 10:00.
        var page = await GetPageAsync(
            client,
            $"?product={product.PublicId}&from={Uri.EscapeDataString("2026-10-01T11:00:00+02:00")}&to=2026-10-01T11:00:00Z");

        Assert.Equal([movements[2].PublicId, movements[1].PublicId], page.Ids);
    }

    [Fact]
    public async Task FiltersCombine()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var actor = await CreateUserAsync(factory, Roles.Staff);
        var product = TestProducts.New($"P-{Guid.NewGuid():N}");
        var match = Movement(product, actor, StockMovementType.Issue, T0.AddHours(2), delta: -1);
        await TestDatabase.AddAsync(
            DatabaseUrl,
            product,
            Movement(product, actor, StockMovementType.Issue, T0, delta: -1),
            Movement(product, actor, StockMovementType.Receive, T0.AddHours(2)),
            match);

        var page = await GetPageAsync(client, $"?product={product.PublicId}&type=issue&actor={actor.PublicId}&from=2026-10-01T09:00:00Z");

        Assert.Equal([match.PublicId], page.Ids);
    }

    [Theory]
    [InlineData("product")]
    [InlineData("actor")]
    public async Task AnUnknownProductOrActorMatchesNothing(string filter)
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        await SaveMovementsAsync(1, _ => T0);

        var page = await GetPageAsync(client, $"?{filter}={Guid.NewGuid()}");

        Assert.Empty(page.Ids);
        Assert.Null(page.NextCursor);
    }

    [Theory]
    [InlineData("?type=transfer", "type")]
    [InlineData("?type=Issue", "type")]
    [InlineData("?type=1", "type")]
    [InlineData("?cursor=not-a-cursor", "cursor")]
    [InlineData("?cursor=MTIzNA", "cursor")]
    [InlineData("?limit=0", "limit")]
    [InlineData("?limit=101", "limit")]
    [InlineData("?from=2026-10-02T00:00:00Z&to=2026-10-01T00:00:00Z", "to")]
    [InlineData("?from=2026-10-01T00:00:00Z&to=2026-10-01T00:00:00Z", "to")]
    public async Task InvalidParametersAreRejectedWithTheField(string query, string field)
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);

        using var response = await client.GetAsync(new Uri("/api/v1/stock/movements" + query, UriKind.Relative), Token);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        var error = document.RootElement.GetProperty("error");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", error.GetProperty("code").GetString());
        Assert.Equal([field], error.GetProperty("details").GetProperty("fields").EnumerateObject().Select(p => p.Name));
    }

    [Theory]
    [InlineData("?product=42")]
    [InlineData("?from=yesterday")]
    public async Task ParametersThatDoNotParseAreBadRequests(string query)
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);

        using var response = await client.GetAsync(new Uri("/api/v1/stock/movements" + query, UriKind.Relative), Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("bad_request", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task AnonymousCallersCannotReadTheLedger()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/stock/movements", UriKind.Relative), Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private string DatabaseUrl => factory.Settings[StockroomOptions.DatabaseUrlKey]!;

    private sealed record Page(List<JsonElement> Items, string? NextCursor)
    {
        public List<Guid> Ids => Items.Select(i => i.GetProperty("id").GetGuid()).ToList();
    }

    private static async Task<Page> GetPageAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync(new Uri("/api/v1/stock/movements" + query, UriKind.Relative), Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        var root = document.RootElement;
        Assert.Equal(["items", "next_cursor"], root.EnumerateObject().Select(p => p.Name));
        return new Page(
            root.GetProperty("items").EnumerateArray().Select(i => i.Clone()).ToList(),
            root.GetProperty("next_cursor").GetString());
    }

    /// <summary>Saves a new product with <paramref name="count"/> receive movements, the i-th at <paramref name="createdAt"/>(i).</summary>
    private async Task<(Product Product, List<StockMovement> Movements)> SaveMovementsAsync(int count, Func<int, DateTimeOffset> createdAt)
    {
        var actor = await CreateUserAsync(factory, Roles.Staff);
        var product = TestProducts.New($"P-{Guid.NewGuid():N}");
        var movements = Enumerable.Range(0, count)
            .Select(i => Movement(product, actor, StockMovementType.Receive, createdAt(i), quantityAfter: i + 1))
            .ToList();
        await TestDatabase.AddAsync(DatabaseUrl, [product, .. movements]);
        return (product, movements);
    }

    private static StockMovement Movement(
        Product product,
        User actor,
        StockMovementType type,
        DateTimeOffset createdAt,
        int delta = 1,
        int quantityAfter = 1,
        StockMovement? voids = null) =>
        new()
        {
            Id = TestDatabase.Ids.NewInternalId(),
            PublicId = TestDatabase.Ids.NewPublicId(),
            ProductId = product.Id,
            LocationId = Location.MainStorageId,
            Type = type,
            Delta = delta,
            QuantityAfter = quantityAfter,
            Reason = StockMovementReason.Other,
            VoidsMovementId = voids?.Id,
            ActorId = actor.Id,
            CreatedAt = createdAt,
        };
}
