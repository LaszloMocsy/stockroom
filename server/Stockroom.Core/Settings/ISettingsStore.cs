namespace Stockroom.Core.Settings;

/// <summary>Reads and writes the settings in <see cref="StockroomSettings"/>.</summary>
public interface ISettingsStore
{
    /// <summary>The stored value, or the setting's default when it was never written.</summary>
    Task<T> GetAsync<T>(Setting<T> setting, CancellationToken cancellationToken);

    /// <summary>Stores <paramref name="value"/>, replacing any earlier value.</summary>
    Task SetAsync<T>(Setting<T> setting, T value, CancellationToken cancellationToken);
}
