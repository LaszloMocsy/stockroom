using Microsoft.EntityFrameworkCore;

namespace Stockroom.Data;

public static class DatabaseMigrator
{
    /// <summary>
    /// Brings the database up to this build's schema (spec 12.3): applies every pending migration,
    /// creating the schema on an empty database. Migrations are forward-only, so a database that has
    /// migrations this build does not know comes from a newer server version; it is left untouched
    /// and <see cref="DatabaseSchemaTooNewException"/> is thrown.
    /// </summary>
    public static async Task MigrateAsync(StockroomDbContext db, CancellationToken cancellationToken)
    {
        var known = db.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
        var applied = await db.Database.GetAppliedMigrationsAsync(cancellationToken);
        var unknown = applied.Where(m => !known.Contains(m)).ToList();
        if (unknown.Count > 0)
        {
            throw new DatabaseSchemaTooNewException(unknown);
        }

        await db.Database.MigrateAsync(cancellationToken);
    }
}
