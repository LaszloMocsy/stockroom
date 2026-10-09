namespace Stockroom.Data.Stock;

/// <summary>
/// The quantity changed after the user saw it, so their count may be out of date (spec 3.2, rule 5).
/// Nothing was written; the user confirms against <see cref="Current"/> and tries again.
/// </summary>
public sealed class StockConflictException(int expected, int current)
    : Exception($"The quantity is now {current}, not the expected {expected}.")
{
    /// <summary>The quantity the user saw.</summary>
    public int Expected { get; } = expected;

    /// <summary>The quantity on hand now.</summary>
    public int Current { get; } = current;
}
