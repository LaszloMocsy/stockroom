using Microsoft.EntityFrameworkCore;
using Npgsql;
using Stockroom.Core.Identifiers;
using Stockroom.Data;

namespace Stockroom.Tests.Infrastructure;

/// <summary>Direct <see cref="StockroomDbContext"/> access for data-layer tests, without the API host.</summary>
public static class TestDatabase
{
    public static readonly IdGenerator Ids = new(TimeProvider.System);

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

    /// <summary>Saves <paramref name="entities"/> in one fresh context and one <c>SaveChanges</c>.</summary>
    public static async Task AddAsync(string databaseUrl, params object[] entities)
    {
        await using var db = CreateContext(databaseUrl);
        db.AddRange(entities);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public static void AssertConstraintViolation(DbUpdateException ex, string sqlState, string constraint)
    {
        var postgresError = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal(sqlState, postgresError.SqlState);
        Assert.Equal(constraint, postgresError.ConstraintName);
    }
}
