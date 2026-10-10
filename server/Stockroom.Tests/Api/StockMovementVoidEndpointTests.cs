using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Stockroom.Api.Configuration;
using Stockroom.Core.Locations;
using Stockroom.Core.Products;
using Stockroom.Core.Settings;
using Stockroom.Core.Stock;
using Stockroom.Core.Users;
using Stockroom.Data.Settings;
using Stockroom.Data.Stock;
using Stockroom.Tests.Data;
using Stockroom.Tests.Infrastructure;
using static Stockroom.Tests.Api.TestAuth;

namespace Stockroom.Tests.Api;

/// <summary>
/// <c>POST /api/v1/stock/movements/:id/void</c>. Movements to void are recorded straight through
/// <see cref="StockService"/> with a clock set back, so each test controls how old they are.
/// </summary>
public sealed class StockMovementVoidEndpointTests(StockroomApiFactory factory, PostgresFixture postgres) : IClassFixture<StockroomApiFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string DatabaseUrl => factory.Settings[StockroomOptions.DatabaseUrlKey]!;

    [Fact]
    public async Task AnAdminVoidsAnotherUsersOldMovement()
    {
        var (client, admin) = await CreateClientWithUserAsync(factory, Roles.Admin);
        using var _ = client;
        var staff = await CreateUserAsync(factory, Roles.Staff);
        var product = await CreateProductAsync(DatabaseUrl);
        var received = await ReceiveAsync(DatabaseUrl, product, staff, 10, ago: TimeSpan.FromDays(30));

        using var response = await VoidAsync(client, received.PublicId);
        var body = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("void", body.GetProperty("type").GetString());
        Assert.Equal(-10, body.GetProperty("delta").GetInt32());
        Assert.Equal(0, body.GetProperty("quantity_after").GetInt32());
        Assert.Equal("correction", body.GetProperty("reason").GetString());
        Assert.Equal(received.PublicId, body.GetProperty("voids_movement_id").GetGuid());
        Assert.Equal(product.PublicId, body.GetProperty("product_id").GetGuid());
        Assert.Equal(admin.PublicId, body.GetProperty("actor_id").GetGuid());
        Assert.Equal(0, await QuantityAsync(product));
    }

    [Fact]
    public async Task TheActorUndoesTheirOwnMovementWithinTheUndoWindow()
    {
        var (client, staff) = await CreateClientWithUserAsync(factory, Roles.Staff, "Anna Staff");
        using var _ = client;
        var product = await CreateProductAsync(DatabaseUrl);
        await ReceiveAsync(DatabaseUrl, product, staff, 10, ago: TimeSpan.FromHours(1));
        var issued = await IssueAsync(product, staff, 1, ago: TimeSpan.FromMinutes(4));

        using var response = await VoidAsync(client, issued.PublicId);
        var body = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal((1, 10), (body.GetProperty("delta").GetInt32(), body.GetProperty("quantity_after").GetInt32()));
        Assert.Equal(staff.PublicId, body.GetProperty("actor_id").GetGuid());
        Assert.Equal("Anna Staff", body.GetProperty("actor_name").GetString());
        Assert.Equal((product.Sku, product.Name), (body.GetProperty("product_sku").GetString(), body.GetProperty("product_name").GetString()));
    }

    [Fact]
    public async Task TheActorCannotUndoOnceTheUndoWindowHasPassed()
    {
        var (client, staff) = await CreateClientWithUserAsync(factory, Roles.Staff);
        using var _ = client;
        var product = await CreateProductAsync(DatabaseUrl);
        var received = await ReceiveAsync(DatabaseUrl, product, staff, 10, ago: TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));

        using var response = await VoidAsync(client, received.PublicId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("undo_window_expired", await ErrorCodeAsync(response));
        await AssertNotVoidedAsync(received);
    }

    [Fact]
    public async Task StaffCannotVoidAnotherUsersMovementEvenWithinTheUndoWindow()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var other = await CreateUserAsync(factory, Roles.Staff);
        var product = await CreateProductAsync(DatabaseUrl);
        var received = await ReceiveAsync(DatabaseUrl, product, other, 10, ago: TimeSpan.Zero);

        using var response = await VoidAsync(client, received.PublicId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("forbidden", await ErrorCodeAsync(response));
        await AssertNotVoidedAsync(received);
    }

    [Fact]
    public async Task TheUndoWindowIsConfigurable()
    {
        await using var app = await StockroomApiFactory.CreateAsync(postgres, configure: settings =>
            settings[StockroomOptions.UndoWindowSecondsKey] = "60");
        var databaseUrl = app.Settings[StockroomOptions.DatabaseUrlKey]!;
        var (client, staff) = await CreateClientWithUserAsync(app, Roles.Staff);
        using var _ = client;
        var product = await CreateProductAsync(databaseUrl);
        var recent = await ReceiveAsync(databaseUrl, product, staff, 1, ago: TimeSpan.FromSeconds(30));
        var older = await ReceiveAsync(databaseUrl, product, staff, 1, ago: TimeSpan.FromSeconds(90));

        using var recentResponse = await VoidAsync(client, recent.PublicId);
        using var olderResponse = await VoidAsync(client, older.PublicId);

        Assert.Equal(HttpStatusCode.Created, recentResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, olderResponse.StatusCode);
        Assert.Equal("undo_window_expired", await ErrorCodeAsync(olderResponse));
    }

    [Fact]
    public async Task AZeroUndoWindowLeavesVoidingToAdmins()
    {
        await using var app = await StockroomApiFactory.CreateAsync(postgres, configure: settings =>
            settings[StockroomOptions.UndoWindowSecondsKey] = "0");
        var databaseUrl = app.Settings[StockroomOptions.DatabaseUrlKey]!;
        var (client, staff) = await CreateClientWithUserAsync(app, Roles.Staff);
        using var _ = client;
        using var admin = await CreateClientAsAsync(app, Roles.Admin);
        var product = await CreateProductAsync(databaseUrl);
        var received = await ReceiveAsync(databaseUrl, product, staff, 1, ago: TimeSpan.Zero);

        using var staffResponse = await VoidAsync(client, received.PublicId);
        using var adminResponse = await VoidAsync(admin, received.PublicId);

        Assert.Equal(HttpStatusCode.Forbidden, staffResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, adminResponse.StatusCode);
    }

    [Fact]
    public async Task TheReasonAndNoteAreRecorded()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Admin);
        var staff = await CreateUserAsync(factory, Roles.Staff);
        var product = await CreateProductAsync(DatabaseUrl);
        var received = await ReceiveAsync(DatabaseUrl, product, staff, 4, ago: TimeSpan.FromHours(1));

        using var response = await VoidAsync(client, received.PublicId, """{"reason":"damaged","note":"Wrong product scanned"}""");
        var body = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("damaged", body.GetProperty("reason").GetString());
        Assert.Equal("Wrong product scanned", body.GetProperty("note").GetString());
    }

    [Theory]
    [InlineData("""{"reason":"broken"}""")]
    [InlineData("""{"reason":1}""")]
    [InlineData("not json")]
    public async Task AnUnreadableBodyIsRejected(string json)
    {
        using var client = await CreateClientAsAsync(factory, Roles.Admin);
        var staff = await CreateUserAsync(factory, Roles.Staff);
        var product = await CreateProductAsync(DatabaseUrl);
        var received = await ReceiveAsync(DatabaseUrl, product, staff, 4, ago: TimeSpan.Zero);

        using var response = await VoidAsync(client, received.PublicId, json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("bad_request", await ErrorCodeAsync(response));
        await AssertNotVoidedAsync(received);
    }

    [Fact]
    public async Task AnOverlongNoteIsRejected()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Admin);
        var staff = await CreateUserAsync(factory, Roles.Staff);
        var product = await CreateProductAsync(DatabaseUrl);
        var received = await ReceiveAsync(DatabaseUrl, product, staff, 4, ago: TimeSpan.Zero);

        using var response = await VoidAsync(client, received.PublicId, JsonSerializer.Serialize(new { note = new string('x', 2001) }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", await ErrorCodeAsync(response));
        await AssertNotVoidedAsync(received);
    }

    [Fact]
    public async Task AnUnknownMovementIsNotFound()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Admin);

        using var unknown = await VoidAsync(client, Guid.NewGuid());
        using var malformed = await client.PostAsync(new Uri("/api/v1/stock/movements/not-a-guid/void", UriKind.Relative), content: null, Token);

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("not_found", await ErrorCodeAsync(unknown));
        Assert.Equal(HttpStatusCode.NotFound, malformed.StatusCode);
    }

    [Fact]
    public async Task AMovementIsVoidedOnlyOnce()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Admin);
        var staff = await CreateUserAsync(factory, Roles.Staff);
        var product = await CreateProductAsync(DatabaseUrl);
        var received = await ReceiveAsync(DatabaseUrl, product, staff, 4, ago: TimeSpan.Zero);
        using var first = await VoidAsync(client, received.PublicId);

        using var second = await VoidAsync(client, received.PublicId);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("already_voided", await ErrorCodeAsync(second));
        Assert.Equal(0, await QuantityAsync(product));
    }

    [Fact]
    public async Task AVoidCannotBeVoided()
    {
        var (client, staff) = await CreateClientWithUserAsync(factory, Roles.Staff);
        using var _ = client;
        var product = await CreateProductAsync(DatabaseUrl);
        var received = await ReceiveAsync(DatabaseUrl, product, staff, 4, ago: TimeSpan.Zero);
        using var undo = await VoidAsync(client, received.PublicId);
        var voidId = (await ReadAsync(undo)).GetProperty("id").GetGuid();

        using var response = await VoidAsync(client, voidId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("movement_is_void", await ErrorCodeAsync(response));
        Assert.Equal(0, await QuantityAsync(product));
    }

    [Fact]
    public async Task VoidingAReceiveWhoseUnitsAreGoneIsAConflict()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Admin);
        var staff = await CreateUserAsync(factory, Roles.Staff);
        var product = await CreateProductAsync(DatabaseUrl);
        var received = await ReceiveAsync(DatabaseUrl, product, staff, 5, ago: TimeSpan.FromHours(1));
        await IssueAsync(product, staff, 3, ago: TimeSpan.FromMinutes(30));

        using var response = await VoidAsync(client, received.PublicId);
        var error = (await ReadAsync(response)).GetProperty("error");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("insufficient_stock", error.GetProperty("code").GetString());
        Assert.Equal(5, error.GetProperty("details").GetProperty("requested").GetInt32());
        Assert.Equal(2, error.GetProperty("details").GetProperty("available").GetInt32());
        await AssertNotVoidedAsync(received);
    }

    [Fact]
    public async Task ARetryWithTheSameIdempotencyKeyReturnsTheOriginalVoid()
    {
        var (client, staff) = await CreateClientWithUserAsync(factory, Roles.Staff);
        using var _ = client;
        var product = await CreateProductAsync(DatabaseUrl);
        var received = await ReceiveAsync(DatabaseUrl, product, staff, 4, ago: TimeSpan.Zero);
        var key = Guid.NewGuid().ToString();

        using var first = await VoidAsync(client, received.PublicId, idempotencyKey: key);
        using var retry = await VoidAsync(client, received.PublicId, idempotencyKey: key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal((await ReadAsync(first)).GetProperty("id").GetGuid(), (await ReadAsync(retry)).GetProperty("id").GetGuid());
        Assert.Equal(0, await QuantityAsync(product));
    }

    [Fact]
    public async Task ARetryReturnsTheVoidEvenAfterTheUndoWindowHasPassed()
    {
        var (client, staff) = await CreateClientWithUserAsync(factory, Roles.Staff);
        using var _ = client;
        var product = await CreateProductAsync(DatabaseUrl);
        var received = await ReceiveAsync(DatabaseUrl, product, staff, 4, ago: TimeSpan.FromMinutes(20));
        var key = Guid.NewGuid().ToString();
        var undone = await StockServiceAt(DatabaseUrl, TimeSpan.FromMinutes(19), s =>
            s.VoidAsync(new VoidStock(received.Id, staff.Id) { IdempotencyKey = key }, Token));

        using var retry = await VoidAsync(client, received.PublicId, idempotencyKey: key);
        using var withoutKey = await VoidAsync(client, received.PublicId);

        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal(undone.PublicId, (await ReadAsync(retry)).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.Forbidden, withoutKey.StatusCode);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task ABlankOrOverlongIdempotencyKeyIsRejected(string? key)
    {
        using var client = await CreateClientAsAsync(factory, Roles.Admin);
        var staff = await CreateUserAsync(factory, Roles.Staff);
        var product = await CreateProductAsync(DatabaseUrl);
        var received = await ReceiveAsync(DatabaseUrl, product, staff, 4, ago: TimeSpan.Zero);

        using var response = await VoidAsync(client, received.PublicId, idempotencyKey: key ?? new string('k', 256));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", await ErrorCodeAsync(response));
        await AssertNotVoidedAsync(received);
    }

    [Fact]
    public async Task AnonymousCallersAreRejected()
    {
        using var client = factory.CreateClient();
        var staff = await CreateUserAsync(factory, Roles.Staff);
        var product = await CreateProductAsync(DatabaseUrl);
        var received = await ReceiveAsync(DatabaseUrl, product, staff, 4, ago: TimeSpan.Zero);

        using var response = await VoidAsync(client, received.PublicId);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertNotVoidedAsync(received);
    }

    private static async Task<(HttpClient Client, User User)> CreateClientWithUserAsync(StockroomApiFactory app, string role, string displayName = "Test User")
    {
        var user = await CreateUserAsync(app, role, displayName);
        var tokens = await LoginAsync(app, user.UserName!);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return (client, user);
    }

    private static async Task<Product> CreateProductAsync(string databaseUrl)
    {
        var product = TestProducts.New($"P-{Guid.NewGuid():N}");
        await TestDatabase.AddAsync(databaseUrl, product);
        return product;
    }

    private static Task<StockMovement> ReceiveAsync(string databaseUrl, Product product, User actor, int quantity, TimeSpan ago) =>
        StockServiceAt(databaseUrl, ago, s => s.ReceiveAsync(new ReceiveStock(product.Id, quantity, actor.Id), Token));

    private Task<StockMovement> IssueAsync(Product product, User actor, int quantity, TimeSpan ago) =>
        StockServiceAt(DatabaseUrl, ago, s => s.IssueAsync(new IssueStock(product.Id, quantity, actor.Id), Token));

    /// <summary>Runs <paramref name="operation"/> with a stock service whose clock reads <paramref name="ago"/> before now.</summary>
    private static async Task<StockMovement> StockServiceAt(string databaseUrl, TimeSpan ago, Func<StockService, Task<StockMovement>> operation)
    {
        await using var db = TestDatabase.CreateContext(databaseUrl);
        return await operation(new StockService(db, new SettingsStore(db), TestDatabase.Ids, new FakeTimeProvider(DateTimeOffset.UtcNow - ago)));
    }

    private static async Task<HttpResponseMessage> VoidAsync(HttpClient client, Guid movementId, string? json = null, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/api/v1/stock/movements/{movementId}/void", UriKind.Relative))
        {
            Content = json is null ? null : new StringContent(json, Encoding.UTF8, "application/json"),
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

    private async Task AssertNotVoidedAsync(StockMovement movement)
    {
        await using var db = TestDatabase.CreateContext(DatabaseUrl);
        Assert.False(await db.StockMovements.AnyAsync(m => m.VoidsMovementId == movement.Id, Token));
    }
}
