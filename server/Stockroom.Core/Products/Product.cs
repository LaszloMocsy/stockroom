namespace Stockroom.Core.Products;

/// <summary>
/// Something kept in stock (spec 3.1, "Product"). Its quantity is not stored here: stock levels and
/// the movement ledger own it, and only <c>StockService</c> changes them.
/// </summary>
public sealed class Product
{
    /// <summary>Internal UUID v7 primary key. Never exposed.</summary>
    public Guid Id { get; init; }

    /// <summary>Public UUID v4, the only ID clients see.</summary>
    public Guid PublicId { get; init; }

    /// <summary>Human-readable, unique, and immutable once created.</summary>
    public required string Sku { get; init; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>Low-stock threshold; <see langword="null"/> means no alert.</summary>
    public int? MinStock { get; set; }

    /// <summary>Set when the product is archived (soft delete).</summary>
    public DateTimeOffset? ArchivedAt { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Internal ID of the user who created the product, if known.</summary>
    public Guid? CreatedBy { get; init; }
}
