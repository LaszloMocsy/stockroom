using Stockroom.Core.Products;
using Stockroom.Tests.Infrastructure;

namespace Stockroom.Tests.Data;

/// <summary>Builders for the product data-layer tests.</summary>
internal static class TestProducts
{
    /// <summary>A product with only the required fields set, unless given.</summary>
    public static Product New(string sku, Guid? publicId = null, Guid? createdBy = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new Product
        {
            Id = TestDatabase.Ids.NewInternalId(),
            PublicId = publicId ?? TestDatabase.Ids.NewPublicId(),
            Sku = sku,
            Name = "Product " + sku,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = createdBy,
        };
    }

    public static ProductBarcode Barcode(Guid productId, string barcode) =>
        new() { Id = TestDatabase.Ids.NewInternalId(), ProductId = productId, Barcode = barcode };
}
