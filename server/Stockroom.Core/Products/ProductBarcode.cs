namespace Stockroom.Core.Products;

/// <summary>
/// A scannable payload (EAN, UPC, Code128, QR) attached to a product (spec 3.1). A barcode identifies at
/// most one product, so it is unique across all products. It is business data, not a key.
/// </summary>
public sealed class ProductBarcode
{
    /// <summary>Internal UUID v7 primary key. Never exposed.</summary>
    public Guid Id { get; init; }

    /// <summary>Internal ID of the product the barcode belongs to.</summary>
    public Guid ProductId { get; init; }

    /// <summary>The scanned payload, stored exactly as read.</summary>
    public required string Barcode { get; init; }
}
