using Stockroom.Core.Products;
using Stockroom.Data;

namespace Stockroom.Api.Endpoints;

/// <summary>A product with its barcodes and current quantity, identified by its public ID (spec 3.1).</summary>
/// <param name="Id">The product's public ID.</param>
/// <param name="Sku">Human-readable, unique, and immutable.</param>
/// <param name="Name">Display name.</param>
/// <param name="Description">Free text.</param>
/// <param name="Barcodes">Scannable payloads that identify the product, oldest first.</param>
/// <param name="MinStock">Low-stock threshold; <c>null</c> means no alert.</param>
/// <param name="Quantity">Units on hand.</param>
/// <param name="ArchivedAt">When the product was archived; <c>null</c> while it is active.</param>
/// <param name="CreatedAt">When the product was created.</param>
/// <param name="UpdatedAt">When the product's details last changed.</param>
public sealed record ProductResponse(
    Guid Id,
    string Sku,
    string Name,
    string? Description,
    IReadOnlyList<string> Barcodes,
    int? MinStock,
    int Quantity,
    DateTimeOffset? ArchivedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>
    /// <paramref name="products"/> as responses, with the quantity summed over every location in the
    /// database. Filter the products before projecting: EF cannot translate a filter on the projected record.
    /// </summary>
    internal static IQueryable<ProductResponse> Project(IQueryable<Product> products, StockroomDbContext db) =>
        from product in products
        select new ProductResponse(
            product.PublicId,
            product.Sku,
            product.Name,
            product.Description,
            // Internal IDs are UUID v7, so they sort in the order the barcodes were added.
            product.Barcodes.OrderBy(b => b.Id).Select(b => b.Barcode).ToList(),
            product.MinStock,
            db.StockLevels.Where(l => l.ProductId == product.Id).Sum(l => (int?)l.Quantity) ?? 0,
            product.ArchivedAt,
            product.CreatedAt,
            product.UpdatedAt);
}
