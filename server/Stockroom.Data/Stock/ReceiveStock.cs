using Stockroom.Core.Stock;

namespace Stockroom.Data.Stock;

/// <summary>A request to add stock (spec 4.2, "Add stock").</summary>
/// <param name="ProductId">Internal ID of the product.</param>
/// <param name="Quantity">Whole units to add; must be positive.</param>
/// <param name="ActorId">Internal ID of the user making the movement.</param>
public sealed record ReceiveStock(Guid ProductId, int Quantity, Guid ActorId)
{
    public StockMovementReason Reason { get; init; } = StockMovementReason.Purchase;

    public string? Note { get; init; }

    /// <summary>External reference, such as an order number or delivery note.</summary>
    public string? Reference { get; init; }
}
