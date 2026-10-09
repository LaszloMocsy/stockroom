using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stockroom.Core.Locations;
using Stockroom.Core.Products;
using Stockroom.Core.Stock;
using Stockroom.Core.Users;
using Stockroom.Data;
using Stockroom.Data.Settings;
using Stockroom.Data.Stock;
using Stockroom.Tests.Api;
using Stockroom.Tests.Infrastructure;

namespace Stockroom.Tests.Data;

public sealed class StockReconcilerTests(PostgresFixture postgres)
{
    private static readonly User Actor = TestUsers.New("actor");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task StockChangedOnlyThroughTheServiceHasNoDrift()
    {
        var (databaseUrl, first, second) = await CreateDatabaseWithProductsAsync();
        await using var db = TestDatabase.CreateContext(databaseUrl);
        var stock = Service(db);
        await stock.ReceiveAsync(new ReceiveStock(first.Id, 10, Actor.Id), Token);
        var issued = await stock.IssueAsync(new IssueStock(first.Id, 4, Actor.Id), Token);
        await stock.AdjustAsync(new AdjustStock(first.Id, 9, Actor.Id), Token);
        await stock.VoidAsync(new VoidStock(issued.Id, Actor.Id), Token);
        await stock.RecordInitialAsync(new InitialStock(second.Id, 7, Actor.Id), Token);

        Assert.Empty(await new StockReconciler(db).FindDriftAsync(Token));
    }

    [Fact]
    public async Task ACorruptedLevelIsReported()
    {
        var (databaseUrl, product, _) = await CreateDatabaseWithProductsAsync();
        await using var db = TestDatabase.CreateContext(databaseUrl);
        await Service(db).ReceiveAsync(new ReceiveStock(product.Id, 10, Actor.Id), Token);

        await db.Database.ExecuteSqlAsync($"UPDATE stock_levels SET quantity = quantity + 3 WHERE product_id = {product.Id}", Token);

        var drift = Assert.Single(await new StockReconciler(db).FindDriftAsync(Token));
        Assert.Equal(new StockDrift(product.Id, product.PublicId, Location.MainStorageId, 13, 10), drift);
        Assert.Equal(3, drift.Difference);
    }

    [Fact]
    public async Task AMissingLevelIsReported()
    {
        var (databaseUrl, product, _) = await CreateDatabaseWithProductsAsync();
        await using var db = TestDatabase.CreateContext(databaseUrl);
        await Service(db).ReceiveAsync(new ReceiveStock(product.Id, 10, Actor.Id), Token);

        await db.Database.ExecuteSqlAsync($"DELETE FROM stock_levels WHERE product_id = {product.Id}", Token);

        var drift = Assert.Single(await new StockReconciler(db).FindDriftAsync(Token));
        Assert.Equal((0, 10L, -10L), (drift.CachedQuantity, drift.LedgerQuantity, drift.Difference));
    }

    [Fact]
    public async Task ALevelWithoutMovementsIsReported()
    {
        var (databaseUrl, product, _) = await CreateDatabaseWithProductsAsync();
        await TestDatabase.AddAsync(
            databaseUrl, new StockLevel { ProductId = product.Id, LocationId = Location.MainStorageId, Quantity = 4 });

        await using var db = TestDatabase.CreateContext(databaseUrl);
        var drift = Assert.Single(await new StockReconciler(db).FindDriftAsync(Token));
        Assert.Equal((4, 0L), (drift.CachedQuantity, drift.LedgerQuantity));
    }

    [Fact]
    public async Task OnlyTheProductsThatDriftedAreReported()
    {
        var (databaseUrl, first, second) = await CreateDatabaseWithProductsAsync();
        await using var db = TestDatabase.CreateContext(databaseUrl);
        await Service(db).ReceiveAsync(new ReceiveStock(first.Id, 10, Actor.Id), Token);
        await Service(db).ReceiveAsync(new ReceiveStock(second.Id, 5, Actor.Id), Token);

        await db.Database.ExecuteSqlAsync($"UPDATE stock_levels SET quantity = 0 WHERE product_id = {second.Id}", Token);

        var drift = Assert.Single(await new StockReconciler(db).FindDriftAsync(Token));
        Assert.Equal((second.Id, 0, 5L), (drift.ProductId, drift.CachedQuantity, drift.LedgerQuantity));
    }

    [Fact]
    public async Task TheApiProvidesTheReconciler()
    {
        await using var factory = await StockroomApiFactory.CreateAsync(postgres);
        await using var scope = factory.Services.CreateAsyncScope();

        var reconciler = scope.ServiceProvider.GetRequiredService<StockReconciler>();

        Assert.Empty(await reconciler.FindDriftAsync(Token));
    }

    private async Task<(string DatabaseUrl, Product First, Product Second)> CreateDatabaseWithProductsAsync()
    {
        var databaseUrl = await TestDatabase.CreateMigratedAsync(postgres, Token);
        var first = TestProducts.New("SR-000001");
        var second = TestProducts.New("SR-000002");
        await TestDatabase.AddAsync(databaseUrl, Actor, first, second);
        return (databaseUrl, first, second);
    }

    private static StockService Service(StockroomDbContext db) =>
        new(db, new SettingsStore(db), TestDatabase.Ids, TimeProvider.System);
}
