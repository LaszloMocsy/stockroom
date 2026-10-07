using Microsoft.EntityFrameworkCore;

namespace Stockroom.Data;

public static class StockroomDbContextOptions
{
    /// <summary>
    /// Uses PostgreSQL at <paramref name="connectionString"/> with snake_case table and column names
    /// (e.g. <c>stock_levels.public_id</c>), matching the names in the specification.
    /// </summary>
    public static DbContextOptionsBuilder UseStockroomDatabase(this DbContextOptionsBuilder builder, string connectionString) =>
        builder
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(StockroomDbContext).Assembly.GetName().Name))
            .UseSnakeCaseNamingConvention();
}
