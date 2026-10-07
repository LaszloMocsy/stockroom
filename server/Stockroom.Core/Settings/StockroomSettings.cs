namespace Stockroom.Core.Settings;

/// <summary>Every database-stored setting. Read and write them through <see cref="ISettingsStore"/>.</summary>
public static class StockroomSettings
{
    /// <summary>Lets an issue take stock below zero instead of being rejected (spec 3.2, rule 4).</summary>
    public static readonly Setting<bool> AllowNegativeStock = new("allow_negative_stock", Default: false);
}
