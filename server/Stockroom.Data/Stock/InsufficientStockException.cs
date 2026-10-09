namespace Stockroom.Data.Stock;

/// <summary>An issue asked for more units than are on hand (spec 3.2, rule 4). Nothing was written.</summary>
public sealed class InsufficientStockException(int requested, int available)
    : Exception($"Cannot remove {requested} units: only {available} on hand.")
{
    /// <summary>Units the issue asked for.</summary>
    public int Requested { get; } = requested;

    /// <summary>Units on hand when the issue was checked.</summary>
    public int Available { get; } = available;
}
