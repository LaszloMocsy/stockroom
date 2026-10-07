using Microsoft.EntityFrameworkCore;
using Npgsql;
using Stockroom.Core.Locations;
using Stockroom.Core.Stock;
using Stockroom.Tests.Infrastructure;

namespace Stockroom.Tests.Data;

public sealed class StockLevelPersistenceTests(PostgresFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AStockLevelIsPersisted()
    {
        var databaseUrl = await TestDatabase.CreateMigratedAsync(postgres, Token);
        var product = TestProducts.New("SR-000001");
        await TestDatabase.AddAsync(databaseUrl, product, Level(product.Id, Location.MainStorageId, 12));

        await using var db = TestDatabase.CreateContext(databaseUrl);
        var level = await db.StockLevels.SingleAsync(Token);

        Assert.Equivalent(Level(product.Id, Location.MainStorageId, 12), level, strict: true);
    }

    [Fact]
    public async Task AProductHasOneLevelPerLocation()
    {
        var databaseUrl = await TestDatabase.CreateMigratedAsync(postgres, Token);
        var product = TestProducts.New("SR-000001");
        await TestDatabase.AddAsync(databaseUrl, product, Level(product.Id, Location.MainStorageId, 12));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(
            () => TestDatabase.AddAsync(databaseUrl, Level(product.Id, Location.MainStorageId, 3)));

        TestDatabase.AssertConstraintViolation(ex, PostgresErrorCodes.UniqueViolation, "pk_stock_levels");
    }

    [Fact]
    public async Task OnlyThePairIsUnique()
    {
        var databaseUrl = await TestDatabase.CreateMigratedAsync(postgres, Token);
        var first = TestProducts.New("SR-000001");
        var second = TestProducts.New("SR-000002");
        var aisle = new Location { Id = TestDatabase.Ids.NewInternalId(), PublicId = TestDatabase.Ids.NewPublicId(), Name = "Aisle 3" };

        await TestDatabase.AddAsync(
            databaseUrl,
            first,
            second,
            aisle,
            Level(first.Id, Location.MainStorageId, 1),
            Level(first.Id, aisle.Id, 2),
            Level(second.Id, Location.MainStorageId, 3));

        await using var db = TestDatabase.CreateContext(databaseUrl);
        Assert.Equal(3, await db.StockLevels.CountAsync(Token));
    }

    [Fact]
    public async Task AProductWithStockLevelsCannotBeDeleted()
    {
        var databaseUrl = await TestDatabase.CreateMigratedAsync(postgres, Token);
        var product = TestProducts.New("SR-000001");
        await TestDatabase.AddAsync(databaseUrl, product, Level(product.Id, Location.MainStorageId, 12));

        await using var db = TestDatabase.CreateContext(databaseUrl);
        var ex = await Assert.ThrowsAsync<PostgresException>(
            () => db.Products.Where(p => p.Id == product.Id).ExecuteDeleteAsync(Token));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ex.SqlState);
        Assert.Equal("fk_stock_levels_products_product_id", ex.ConstraintName);
    }

    private static StockLevel Level(Guid productId, Guid locationId, int quantity) =>
        new() { ProductId = productId, LocationId = locationId, Quantity = quantity };
}
