using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Stockroom.Api.Configuration;
using Stockroom.Core.Locations;
using Stockroom.Core.Products;
using Stockroom.Core.Users;
using Stockroom.Tests.Data;
using Stockroom.Tests.Infrastructure;
using static Stockroom.Tests.Api.TestAuth;

namespace Stockroom.Tests.Api;

public sealed class StockMovementEndpointTests(StockroomApiFactory factory) : IClassFixture<StockroomApiFactory>
{
    private static readonly Uri MovementsUri = new("/api/v1/stock/movements", UriKind.Relative);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string DatabaseUrl => factory.Settings[StockroomOptions.DatabaseUrlKey]!;

    [Fact]
    public async Task AReceiveAddsStockAndReturnsTheMovement()
    {
        var (client, user) = await CreateClientWithUserAsync(Roles.Staff);
        using var _ = client;
        var product = await CreateProductAsync();

        using var response = await client.PostAsJsonAsync(
            MovementsUri,
            new { product_id = product.PublicId, type = "receive", quantity = 12, note = "First delivery", reference = "DN-1" },
            Token);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        var body = document.RootElement;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal(
            ["id", "product_id", "type", "delta", "quantity_after", "reason", "note", "reference", "voids_movement_id", "actor_id", "created_at"],
            body.EnumerateObject().Select(p => p.Name));
        Assert.Equal(product.PublicId, body.GetProperty("product_id").GetGuid());
        Assert.Equal("receive", body.GetProperty("type").GetString());
        Assert.Equal(12, body.GetProperty("delta").GetInt32());
        Assert.Equal(12, body.GetProperty("quantity_after").GetInt32());
        Assert.Equal("purchase", body.GetProperty("reason").GetString());
        Assert.Equal("First delivery", body.GetProperty("note").GetString());
        Assert.Equal("DN-1", body.GetProperty("reference").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("voids_movement_id").ValueKind);
        Assert.Equal(user.PublicId, body.GetProperty("actor_id").GetGuid());
        Assert.Equal(12, await QuantityAsync(product));

        await using var db = TestDatabase.CreateContext(DatabaseUrl);
        var stored = await db.StockMovements.SingleAsync(m => m.ProductId == product.Id, Token);
        Assert.Equal(stored.PublicId, body.GetProperty("id").GetGuid());
        Assert.Equal(stored.CreatedAt, body.GetProperty("created_at").GetDateTimeOffset());
        Assert.Equal(user.Id, stored.ActorId);
    }

    [Fact]
    public async Task AnIssueRemovesStock()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var product = await CreateProductAsync();
        await PostAsync(client, new { product_id = product.PublicId, type = "receive", quantity = 10 });

        using var response = await PostAsync(client, new { product_id = product.PublicId, type = "issue", quantity = 3, reason = "damaged" });
        var body = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(("issue", -3, 7, "damaged"), (body.GetProperty("type").GetString(), body.GetProperty("delta").GetInt32(), body.GetProperty("quantity_after").GetInt32(), body.GetProperty("reason").GetString()));
        Assert.Equal(7, await QuantityAsync(product));
    }

    [Fact]
    public async Task AnIssueReasonDefaultsToSale()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var product = await CreateProductAsync();
        await PostAsync(client, new { product_id = product.PublicId, type = "receive", quantity = 10 });

        using var response = await PostAsync(client, new { product_id = product.PublicId, type = "issue", quantity = 1 });

        Assert.Equal("sale", (await ReadAsync(response)).GetProperty("reason").GetString());
    }

    [Fact]
    public async Task AnAdjustSetsTheCountedQuantity()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Admin);
        var product = await CreateProductAsync();
        await PostAsync(client, new { product_id = product.PublicId, type = "receive", quantity = 45 });

        using var response = await PostAsync(client, new { product_id = product.PublicId, type = "adjust", target_quantity = 42, expected_current = 45 });
        var body = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(("adjust", -3, 42, "count"), (body.GetProperty("type").GetString(), body.GetProperty("delta").GetInt32(), body.GetProperty("quantity_after").GetInt32(), body.GetProperty("reason").GetString()));
        Assert.Equal(42, await QuantityAsync(product));
    }

    [Fact]
    public async Task AnAdjustToTheCurrentQuantityRecordsNothing()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var product = await CreateProductAsync();
        await PostAsync(client, new { product_id = product.PublicId, type = "receive", quantity = 5 });

        using var response = await PostAsync(client, new { product_id = product.PublicId, type = "adjust", target_quantity = 5, expected_current = 5 });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1, await MovementCountAsync(product));
    }

    [Fact]
    public async Task IssuingMoreThanIsOnHandIsAConflict()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var product = await CreateProductAsync();
        await PostAsync(client, new { product_id = product.PublicId, type = "receive", quantity = 2 });

        using var response = await PostAsync(client, new { product_id = product.PublicId, type = "issue", quantity = 3 });
        var error = (await ReadAsync(response)).GetProperty("error");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("insufficient_stock", error.GetProperty("code").GetString());
        Assert.Equal((3, 2), (error.GetProperty("details").GetProperty("requested").GetInt32(), error.GetProperty("details").GetProperty("available").GetInt32()));
        Assert.Equal(2, await QuantityAsync(product));
        Assert.Equal(1, await MovementCountAsync(product));
    }

    [Fact]
    public async Task AStaleExpectedCurrentIsAConflictWithTheNewValue()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var product = await CreateProductAsync();
        await PostAsync(client, new { product_id = product.PublicId, type = "receive", quantity = 43 });

        using var response = await PostAsync(client, new { product_id = product.PublicId, type = "adjust", target_quantity = 42, expected_current = 45 });
        var error = (await ReadAsync(response)).GetProperty("error");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("quantity_changed", error.GetProperty("code").GetString());
        Assert.Equal((45, 43), (error.GetProperty("details").GetProperty("expected").GetInt32(), error.GetProperty("details").GetProperty("current").GetInt32()));
        Assert.Equal(43, await QuantityAsync(product));
    }

    [Fact]
    public async Task AnUnknownProductIsNotFound()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);

        using var response = await PostAsync(client, new { product_id = Guid.NewGuid(), type = "receive", quantity = 1 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("not_found", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task TheInternalProductIdIsNotAccepted()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var product = await CreateProductAsync();

        using var response = await PostAsync(client, new { product_id = product.Id, type = "receive", quantity = 1 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, await MovementCountAsync(product));
    }

    [Theory]
    [InlineData("""{ "type": "receive", "quantity": 1 }""", "product_id")]
    [InlineData("""{ "product_id": "{product}", "quantity": 1 }""", "type")]
    [InlineData("""{ "product_id": "{product}", "type": "receive" }""", "quantity")]
    [InlineData("""{ "product_id": "{product}", "type": "issue", "quantity": 0 }""", "quantity")]
    [InlineData("""{ "product_id": "{product}", "type": "receive", "quantity": 1000001 }""", "quantity")]
    [InlineData("""{ "product_id": "{product}", "type": "receive", "quantity": 1, "target_quantity": 1 }""", "target_quantity")]
    [InlineData("""{ "product_id": "{product}", "type": "issue", "quantity": 1, "expected_current": 1 }""", "expected_current")]
    [InlineData("""{ "product_id": "{product}", "type": "adjust" }""", "target_quantity")]
    [InlineData("""{ "product_id": "{product}", "type": "adjust", "target_quantity": -1 }""", "target_quantity")]
    [InlineData("""{ "product_id": "{product}", "type": "adjust", "target_quantity": 1, "quantity": 1 }""", "quantity")]
    [InlineData("""{ "product_id": "{product}", "type": "receive", "quantity": 1, "reference": "{long}" }""", "reference")]
    public async Task InvalidRequestsAreRejectedWithTheField(string json, string field)
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var product = await CreateProductAsync();

        using var response = await PostJsonAsync(client, json.Replace("{product}", product.PublicId.ToString(), StringComparison.Ordinal).Replace("{long}", new string('x', 201), StringComparison.Ordinal));
        var error = (await ReadAsync(response)).GetProperty("error");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", error.GetProperty("code").GetString());
        Assert.Equal([field], error.GetProperty("details").GetProperty("fields").EnumerateObject().Select(p => p.Name));
        Assert.Equal(0, await MovementCountAsync(product));
    }

    [Theory]
    [InlineData("""{ "product_id": "{product}", "type": "receive", "quantity": 2.5 }""")]
    [InlineData("""{ "product_id": "{product}", "type": "receive", "quantity": "2" }""")]
    [InlineData("""{ "product_id": "{product}", "type": "void", "quantity": 2 }""")]
    [InlineData("""{ "product_id": "{product}", "type": "receive", "quantity": 2, "reason": "stolen" }""")]
    [InlineData("""{ "product_id": "{product}", "type": 0, "quantity": 2 }""")]
    public async Task ValuesOfTheWrongKindAreRejected(string json)
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var product = await CreateProductAsync();

        using var response = await PostJsonAsync(client, json.Replace("{product}", product.PublicId.ToString(), StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("bad_request", await ErrorCodeAsync(response));
        Assert.Equal(0, await MovementCountAsync(product));
    }

    [Fact]
    public async Task ARetryWithTheSameIdempotencyKeyReturnsTheOriginalMovement()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var product = await CreateProductAsync();
        var request = new { product_id = product.PublicId, type = "receive", quantity = 5 };

        using var first = await PostAsync(client, request, idempotencyKey: "7c1b0d2e");
        using var retried = await PostAsync(client, request, idempotencyKey: "7c1b0d2e");

        Assert.Equal(HttpStatusCode.Created, retried.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(Token), await retried.Content.ReadAsStringAsync(Token));
        Assert.Equal(5, await QuantityAsync(product));
        Assert.Equal(1, await MovementCountAsync(product));
    }

    [Fact]
    public async Task OtherUsersMayUseTheSameIdempotencyKey()
    {
        using var first = await CreateClientAsAsync(factory, Roles.Staff);
        using var second = await CreateClientAsAsync(factory, Roles.Staff);
        var product = await CreateProductAsync();
        var request = new { product_id = product.PublicId, type = "receive", quantity = 5 };

        await PostAsync(first, request, idempotencyKey: "same");
        using var response = await PostAsync(second, request, idempotencyKey: "same");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(10, await QuantityAsync(product));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task ABlankOrOverlongIdempotencyKeyIsRejected(string? key)
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var product = await CreateProductAsync();

        using var response = await PostAsync(client, new { product_id = product.PublicId, type = "receive", quantity = 5 }, idempotencyKey: key ?? new string('k', 256));
        var error = (await ReadAsync(response)).GetProperty("error");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", error.GetProperty("code").GetString());
        Assert.Equal(["idempotency-key"], error.GetProperty("details").GetProperty("fields").EnumerateObject().Select(p => p.Name));
        Assert.Equal(0, await MovementCountAsync(product));
    }

    [Fact]
    public async Task AnonymousCallersCannotMoveStock()
    {
        using var client = factory.CreateClient();
        var product = await CreateProductAsync();

        using var response = await PostAsync(client, new { product_id = product.PublicId, type = "receive", quantity = 5 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, await MovementCountAsync(product));
    }

    private async Task<(HttpClient Client, User User)> CreateClientWithUserAsync(string role)
    {
        var user = await CreateUserAsync(factory, role);
        var tokens = await LoginAsync(factory, user.UserName!);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return (client, user);
    }

    private async Task<Product> CreateProductAsync()
    {
        var product = TestProducts.New($"P-{Guid.NewGuid():N}");
        await TestDatabase.AddAsync(DatabaseUrl, product);
        return product;
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, object body, string? idempotencyKey = null) =>
        PostJsonAsync(client, JsonSerializer.Serialize(body), idempotencyKey);

    private static async Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string json, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, MovementsUri)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return await client.SendAsync(request, Token);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return document.RootElement.Clone();
    }

    private async Task<int> QuantityAsync(Product product)
    {
        await using var db = TestDatabase.CreateContext(DatabaseUrl);
        return await db.StockLevels
            .Where(l => l.ProductId == product.Id && l.LocationId == Location.MainStorageId)
            .Select(l => l.Quantity)
            .SingleAsync(Token);
    }

    private async Task<int> MovementCountAsync(Product product)
    {
        await using var db = TestDatabase.CreateContext(DatabaseUrl);
        return await db.StockMovements.CountAsync(m => m.ProductId == product.Id, Token);
    }
}
