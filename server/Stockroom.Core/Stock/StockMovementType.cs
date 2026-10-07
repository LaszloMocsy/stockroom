namespace Stockroom.Core.Stock;

/// <summary>What a movement does (spec 3.1). <c>transfer_out</c> and <c>transfer_in</c> follow with locations (P1).</summary>
public enum StockMovementType
{
    Receive,
    Issue,
    Adjust,
    Initial,
    Void,
}
