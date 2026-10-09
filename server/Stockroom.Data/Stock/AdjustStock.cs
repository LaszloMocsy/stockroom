using Stockroom.Core.Stock;

namespace Stockroom.Data.Stock;

/// <summary>A request to set the quantity to a counted value (spec 4.2, "Manual edit").</summary>
/// <param name="ProductId">Internal ID of the product.</param>
/// <param name="TargetQuantity">The counted quantity; must not be negative.</param>
/// <param name="ActorId">Internal ID of the user making the movement.</param>
public sealed record AdjustStock(Guid ProductId, int TargetQuantity, Guid ActorId)
{
    /// <summary>
    /// The quantity the user saw before counting (spec 3.2, rule 5). If it has changed since, the adjust is
    /// refused; <see langword="null"/> skips the check.
    /// </summary>
    public int? ExpectedCurrent { get; init; }

    public StockMovementReason Reason { get; init; } = StockMovementReason.Count;

    public string? Note { get; init; }

    /// <summary>External reference, such as a stocktake sheet.</summary>
    public string? Reference { get; init; }

    /// <summary>
    /// Client-supplied key that makes a retry of this request return the original movement instead of
    /// writing another (spec 3.2, rule 8). Unique per actor.
    /// </summary>
    public string? IdempotencyKey { get; init; }
}
