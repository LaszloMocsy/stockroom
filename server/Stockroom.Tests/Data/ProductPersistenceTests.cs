using Microsoft.EntityFrameworkCore;
using Npgsql;
using Stockroom.Core.Products;
using Stockroom.Tests.Infrastructure;

namespace Stockroom.Tests.Data;

public sealed class ProductPersistenceTests(PostgresFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AProductIsPersistedWithEveryField()
    {
        var databaseUrl = await TestDatabase.CreateMigratedAsync(postgres, Token);
        var createdAt = new DateTimeOffset(2026, 10, 7, 9, 30, 0, TimeSpan.Zero);
        var product = new Product
        {
            Id = TestProducts.Ids.NewInternalId(),
            PublicId = TestProducts.Ids.NewPublicId(),
            Sku = "SR-000001",
            Name = "M6 hex bolt",
            Description = "Zinc plated, 30 mm",
            MinStock = 50,
            ArchivedAt = createdAt.AddDays(2),
            CreatedAt = createdAt,
            UpdatedAt = createdAt.AddDays(1),
            CreatedBy = TestProducts.Ids.NewInternalId(),
        };

        await using (var db = TestDatabase.CreateContext(databaseUrl))
        {
            db.Products.Add(product);
            await db.SaveChangesAsync(Token);
        }

        await using (var db = TestDatabase.CreateContext(databaseUrl))
        {
            var loaded = await db.Products.SingleAsync(p => p.PublicId == product.PublicId, Token);
            Assert.Equivalent(product, loaded, strict: true);
        }
    }

    [Fact]
    public async Task OptionalFieldsMayBeEmpty()
    {
        var databaseUrl = await TestDatabase.CreateMigratedAsync(postgres, Token);
        var product = TestProducts.New("SR-000001");

        await using (var db = TestDatabase.CreateContext(databaseUrl))
        {
            db.Products.Add(product);
            await db.SaveChangesAsync(Token);
        }

        await using (var db = TestDatabase.CreateContext(databaseUrl))
        {
            var loaded = await db.Products.SingleAsync(p => p.Id == product.Id, Token);
            Assert.Null(loaded.Description);
            Assert.Null(loaded.MinStock);
            Assert.Null(loaded.ArchivedAt);
            Assert.Null(loaded.CreatedBy);
        }
    }

    [Fact]
    public async Task SkusAreUnique()
    {
        var databaseUrl = await TestDatabase.CreateMigratedAsync(postgres, Token);
        await TestProducts.AddAsync(databaseUrl, TestProducts.New("SR-000001"));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => TestProducts.AddAsync(databaseUrl, TestProducts.New("SR-000001")));

        TestProducts.AssertConstraintViolation(ex, PostgresErrorCodes.UniqueViolation, "ix_products_sku");
    }

    [Fact]
    public async Task PublicIdsAreUnique()
    {
        var databaseUrl = await TestDatabase.CreateMigratedAsync(postgres, Token);
        var first = TestProducts.New("SR-000001");
        await TestProducts.AddAsync(databaseUrl, first);

        var duplicate = TestProducts.New("SR-000002", publicId: first.PublicId);
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => TestProducts.AddAsync(databaseUrl, duplicate));

        TestProducts.AssertConstraintViolation(ex, PostgresErrorCodes.UniqueViolation, "ix_products_public_id");
    }
}
