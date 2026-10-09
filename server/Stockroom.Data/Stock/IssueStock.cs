using Stockroom.Core.Stock;

namespace Stockroom.Data.Stock;

/// <summary>A request to remove stock (spec 4.2, "Remove stock").</summary>
/// <param name="ProductId">Internal ID of the product.</param>
/// <param name="Quantity">Whole units to remove; must be positive.</param>
/// <param name="ActorId">Internal ID of the user making the movement.</param>
public sealed record IssueStock(Guid ProductId, int Quantity, Guid ActorId)
{
    public StockMovementReason Reason { get; init; } = StockMovementReason.Sale;

    public string? Note { get; init; }

    /// <summary>External reference, such as an order number or delivery note.</summary>
    public string? Reference { get; init; }
}
