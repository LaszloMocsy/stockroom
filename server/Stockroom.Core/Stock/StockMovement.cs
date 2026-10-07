namespace Stockroom.Core.Stock;

/// <summary>
/// One entry in the append-only stock ledger (spec 3.1, "Stock movement"). Movements are never edited or
/// deleted: a mistake is corrected by a new movement, and only <c>StockService</c> writes them (spec 3.2).
/// </summary>
public sealed class StockMovement
{
    /// <summary>Internal UUID v7 primary key. Never exposed.</summary>
    public Guid Id { get; init; }

    /// <summary>Public UUID v4, the only ID clients see.</summary>
    public Guid PublicId { get; init; }

    /// <summary>Internal ID of the product.</summary>
    public Guid ProductId { get; init; }

    /// <summary>Internal ID of the location.</summary>
    public Guid LocationId { get; init; }

    public StockMovementType Type { get; init; }

    /// <summary>Signed change applied to the quantity.</summary>
    public int Delta { get; init; }

    /// <summary>Quantity at the location after this movement.</summary>
    public int QuantityAfter { get; init; }

    public StockMovementReason Reason { get; init; }

    public string? Note { get; init; }

    /// <summary>External reference, such as an order number or delivery note.</summary>
    public string? Reference { get; init; }

    /// <summary>On a <see cref="StockMovementType.Void"/> movement, the internal ID of the movement it reverses.</summary>
    public Guid? VoidsMovementId { get; init; }

    /// <summary>Client-supplied key, unique per actor, that makes a retried request a no-op.</summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>Internal ID of the user who made the movement.</summary>
    public Guid ActorId { get; init; }

    /// <summary>Server-assigned time of the movement.</summary>
    public DateTimeOffset CreatedAt { get; init; }
}
