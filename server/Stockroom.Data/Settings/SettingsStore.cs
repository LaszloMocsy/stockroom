using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Stockroom.Core.Settings;

namespace Stockroom.Data.Settings;

/// <summary>
/// Stores each setting as a JSON value keyed by its name. Shares the caller's
/// <see cref="StockroomDbContext"/>, so reads and writes join any transaction in progress.
/// </summary>
public sealed class SettingsStore(StockroomDbContext db) : ISettingsStore
{
    public async Task<T> GetAsync<T>(Setting<T> setting, CancellationToken cancellationToken)
    {
        var json = await db.Settings
            .Where(s => s.Key == setting.Key)
            .Select(s => s.Value)
            .SingleOrDefaultAsync(cancellationToken);

        return json is null ? setting.Default : JsonSerializer.Deserialize<T>(json)!;
    }

    public async Task SetAsync<T>(Setting<T> setting, T value, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(value);

        // An upsert, so concurrent first writes of the same setting cannot collide.
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO settings (key, value) VALUES ({setting.Key}, {json}::jsonb)
            ON CONFLICT (key) DO UPDATE SET value = excluded.value
            """,
            cancellationToken);
    }
}
