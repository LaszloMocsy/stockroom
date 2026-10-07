namespace Stockroom.Data.Settings;

/// <summary>One row of the <c>settings</c> table. Use <see cref="SettingsStore"/> rather than this directly.</summary>
public sealed class StoredSetting
{
    public required string Key { get; init; }

    /// <summary>The value as JSON.</summary>
    public required string Value { get; set; }
}
