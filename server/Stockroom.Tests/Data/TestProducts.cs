using Microsoft.EntityFrameworkCore;
using Npgsql;
using Stockroom.Core.Identifiers;
using Stockroom.Core.Products;
using Stockroom.Tests.Infrastructure;

namespace Stockroom.Tests.Data;

/// <summary>Builders and assertions shared by the product data-layer tests.</summary>
internal static class TestProducts
{
    public static readonly IdGenerator Ids = new(TimeProvider.System);

    /// <summary>A product with only the required fields set.</summary>
    public static Product New(string sku, Guid? publicId = null)
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

    public static ProductBarcode Barcode(Guid productId, string barcode) =>
        new() { Id = Ids.NewInternalId(), ProductId = productId, Barcode = barcode };

    /// <summary>Saves <paramref name="entities"/> in one fresh context and one <c>SaveChanges</c>.</summary>
    public static async Task AddAsync(string databaseUrl, params object[] entities)
    {
        await using var db = TestDatabase.CreateContext(databaseUrl);
        db.AddRange(entities);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public static void AssertConstraintViolation(DbUpdateException ex, string sqlState, string constraint)
    {
        var postgresError = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal(sqlState, postgresError.SqlState);
        Assert.Equal(constraint, postgresError.ConstraintName);
    }
}
