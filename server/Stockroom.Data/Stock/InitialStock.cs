using Stockroom.Core.Stock;

namespace Stockroom.Data.Stock;

/// <summary>The quantity a new product starts with (spec 4.2, "Initial quantity on product creation").</summary>
/// <param name="ProductId">Internal ID of the product.</param>
/// <param name="Quantity">Whole units on hand; must be positive.</param>
/// <param name="ActorId">Internal ID of the user creating the product.</param>
public sealed record InitialStock(Guid ProductId, int Quantity, Guid ActorId)
{
    /// <summary>Defaults to <see cref="StockMovementReason.Count"/>: the starting quantity is what is on the shelf.</summary>
    public StockMovementReason Reason { get; init; } = StockMovementReason.Count;

    public string? Note { get; init; }

    /// <summary>External reference, such as a delivery note.</summary>
    public string? Reference { get; init; }
}
