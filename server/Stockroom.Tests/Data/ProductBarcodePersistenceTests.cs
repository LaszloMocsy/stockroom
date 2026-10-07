using Microsoft.EntityFrameworkCore;
using Npgsql;
using Stockroom.Tests.Infrastructure;

namespace Stockroom.Tests.Data;

public sealed class ProductBarcodePersistenceTests(PostgresFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AProductKeepsSeveralBarcodes()
    {
        var databaseUrl = await TestDatabase.CreateMigratedAsync(postgres, Token);
        var product = TestProducts.New("SR-000001");
        product.Barcodes.Add(TestProducts.Barcode(product.Id, "4006381333931"));
        product.Barcodes.Add(TestProducts.Barcode(product.Id, "https://example.com/p/42"));
        await TestProducts.AddAsync(databaseUrl, product);

        await using var db = TestDatabase.CreateContext(databaseUrl);
        var loaded = await db.Products.Include(p => p.Barcodes).SingleAsync(p => p.Id == product.Id, Token);

        Assert.Equivalent(product.Barcodes, loaded.Barcodes, strict: true);
    }

    [Fact]
    public async Task ABarcodeCannotBelongToTwoProducts()
    {
        var databaseUrl = await TestDatabase.CreateMigratedAsync(postgres, Token);
        var first = TestProducts.New("SR-000001");
        var second = TestProducts.New("SR-000002");
        await TestProducts.AddAsync(databaseUrl, first, second, TestProducts.Barcode(first.Id, "4006381333931"));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(
            () => TestProducts.AddAsync(databaseUrl, TestProducts.Barcode(second.Id, "4006381333931")));

        TestProducts.AssertConstraintViolation(ex, PostgresErrorCodes.UniqueViolation, "ix_product_barcodes_barcode");
    }

    [Fact]
    public async Task ABarcodeCannotBeAttachedTwiceToTheSameProduct()
    {
        var databaseUrl = await TestDatabase.CreateMigratedAsync(postgres, Token);
        var product = TestProducts.New("SR-000001");
        await TestProducts.AddAsync(databaseUrl, product, TestProducts.Barcode(product.Id, "4006381333931"));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(
            () => TestProducts.AddAsync(databaseUrl, TestProducts.Barcode(product.Id, "4006381333931")));

        TestProducts.AssertConstraintViolation(ex, PostgresErrorCodes.UniqueViolation, "ix_product_barcodes_barcode");
    }

    [Fact]
    public async Task ABarcodeMustBelongToAnExistingProduct()
    {
        var databaseUrl = await TestDatabase.CreateMigratedAsync(postgres, Token);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(
            () => TestProducts.AddAsync(databaseUrl, TestProducts.Barcode(TestProducts.Ids.NewInternalId(), "4006381333931")));

        TestProducts.AssertConstraintViolation(ex, PostgresErrorCodes.ForeignKeyViolation, "fk_product_barcodes_products_product_id");
    }
}
