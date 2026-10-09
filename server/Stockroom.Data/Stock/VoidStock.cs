using Stockroom.Core.Stock;

namespace Stockroom.Data.Stock;

/// <summary>A request to reverse a movement (spec 3.2, rule 2), e.g. the undo after a quick +1 / −1.</summary>
/// <param name="MovementId">Internal ID of the movement to reverse.</param>
/// <param name="ActorId">Internal ID of the user voiding it.</param>
public sealed record VoidStock(Guid MovementId, Guid ActorId)
{
    public StockMovementReason Reason { get; init; } = StockMovementReason.Correction;

    public string? Note { get; init; }
}
