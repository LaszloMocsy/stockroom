using Microsoft.EntityFrameworkCore;
using Stockroom.Data;

namespace Stockroom.Tests.Infrastructure;

/// <summary>Direct <see cref="StockroomDbContext"/> access for data-layer tests, without the API host.</summary>
public static class TestDatabase
{
    /// <summary>Creates a new database with every migration applied and returns its connection string.</summary>
    public static async Task<string> CreateMigratedAsync(PostgresFixture postgres, CancellationToken cancellationToken)
    {
        var databaseUrl = await postgres.CreateDatabaseAsync(cancellationToken);
        await using var db = CreateContext(databaseUrl);
        await db.Database.MigrateAsync(cancellationToken);
        return databaseUrl;
    }

    public static StockroomDbContext CreateContext(string databaseUrl)
    {
        var options = new DbContextOptionsBuilder<StockroomDbContext>();
        options.UseStockroomDatabase(databaseUrl);
        return new StockroomDbContext(options.Options);
    }
}
