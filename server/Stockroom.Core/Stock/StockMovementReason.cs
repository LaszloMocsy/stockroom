namespace Stockroom.Core.Stock;

/// <summary>Why a movement happened (spec 3.1).</summary>
public enum StockMovementReason
{
    Purchase,
    Sale,
    Return,
    Damaged,
    Lost,
    Found,
    Correction,
    Count,
    Other,
}
