using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Stockroom.Data;

/// <summary>
/// Creates the context for <c>dotnet ef</c> (migrations add/list/remove, database update) without
/// starting the API. Uses <c>STOCKROOM_DATABASE_URL</c> when set, otherwise the local development
/// database from <c>appsettings.Development.json</c>. Adding a migration does not connect to it.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<StockroomDbContext>
{
    private const string DevelopmentDatabaseUrl = "Host=localhost;Database=stockroom;Username=stockroom;Password=stockroom";

    public StockroomDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("STOCKROOM_DATABASE_URL");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = DevelopmentDatabaseUrl;
        }

        var builder = new DbContextOptionsBuilder<StockroomDbContext>();
        builder.UseStockroomDatabase(connectionString);
        return new StockroomDbContext(builder.Options);
    }
}
