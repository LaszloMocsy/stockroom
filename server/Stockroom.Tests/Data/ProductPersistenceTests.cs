using Microsoft.EntityFrameworkCore;
using Npgsql;
using Stockroom.Core.Identifiers;
using Stockroom.Core.Products;
using Stockroom.Tests.Infrastructure;

namespace Stockroom.Tests.Data;

public sealed class ProductPersistenceTests(PostgresFixture postgres)
{
    private static readonly IdGenerator Ids = new(TimeProvider.System);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AProductIsPersistedWithEveryField()
    {
        var databaseUrl = await TestDatabase.CreateMigratedAsync(postgres, Token);
        var createdAt = new DateTimeOffset(2026, 10, 7, 9, 30, 0, TimeSpan.Zero);
        var product = new Product
        {
            Id = Ids.NewInternalId(),
            PublicId = Ids.NewPublicId(),
            Sku = "SR-000001",
            Name = "M6 hex bolt",
            Description = "Zinc plated, 30 mm",
            MinStock = 50,
            ArchivedAt = createdAt.AddDays(2),
            CreatedAt = createdAt,
            UpdatedAt = createdAt.AddDays(1),
            CreatedBy = Ids.NewInternalId(),
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
        var product = NewProduct("SR-000001");

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
        await AddAsync(databaseUrl, NewProduct("SR-000001"));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => AddAsync(databaseUrl, NewProduct("SR-000001")));

        AssertUniqueViolation(ex, "ix_products_sku");
    }

    [Fact]
    public async Task PublicIdsAreUnique()
    {
        var databaseUrl = await TestDatabase.CreateMigratedAsync(postgres, Token);
        var first = NewProduct("SR-000001");
        await AddAsync(databaseUrl, first);

        var duplicate = NewProduct("SR-000002", publicId: first.PublicId);
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => AddAsync(databaseUrl, duplicate));

        AssertUniqueViolation(ex, "ix_products_public_id");
    }

    private static Product NewProduct(string sku, Guid? publicId = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new Product
        {
            Id = Ids.NewInternalId(),
            PublicId = publicId ?? Ids.NewPublicId(),
            Sku = sku,
            Name = "Product " + sku,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private static async Task AddAsync(string databaseUrl, Product product)
    {
        await using var db = TestDatabase.CreateContext(databaseUrl);
        db.Products.Add(product);
        await db.SaveChangesAsync(Token);
    }

    private static void AssertUniqueViolation(DbUpdateException ex, string constraint)
    {
        var postgresError = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresError.SqlState);
        Assert.Equal(constraint, postgresError.ConstraintName);
    }
}
