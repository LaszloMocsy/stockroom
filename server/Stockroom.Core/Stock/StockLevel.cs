namespace Stockroom.Core.Stock;

/// <summary>
/// The quantity of a product at a location (spec 3.1, "Stock level"). It is a cache of the movement
/// ledger: only <c>StockService</c> writes it, in the same transaction as the movement (spec 3.2), and it
/// can be rebuilt from the ledger. Never addressed through the API, so it has no IDs of its own; the
/// product and location pair is its key.
/// </summary>
public sealed class StockLevel
{
    /// <summary>Internal ID of the product.</summary>
    public Guid ProductId { get; init; }

    /// <summary>Internal ID of the location.</summary>
    public Guid LocationId { get; init; }

    /// <summary>Whole units on hand. Negative only when <c>allow_negative_stock</c> is enabled.</summary>
    public int Quantity { get; set; }
}
