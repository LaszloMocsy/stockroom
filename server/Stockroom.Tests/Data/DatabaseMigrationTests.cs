using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Stockroom.Data;
using Stockroom.Tests.Api;
using Stockroom.Tests.Infrastructure;

namespace Stockroom.Tests.Data;

public sealed class DatabaseMigrationTests(PostgresFixture postgres)
{
    private const string NewerMigration = "99991231235959_FromANewerVersion";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task StartupMigratesAnEmptyDatabase()
    {
        await using var factory = await StockroomApiFactory.CreateAsync(postgres);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StockroomDbContext>();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync(Token));
        Assert.Equal(db.Database.GetMigrations(), await db.Database.GetAppliedMigrationsAsync(Token));
        Assert.True(await HistoryTableExistsAsync(db));
    }

    [Fact]
    public async Task StartupRefusesADatabaseFromANewerVersion()
    {
        var databaseUrl = await postgres.CreateDatabaseAsync(Token);
        await MarkAsMigratedByANewerVersionAsync(databaseUrl);

        var result = await ApiProcess.RunAsync(StockroomApiFactory.SettingsFor(databaseUrl), Token);

        Assert.Equal(1, result.ExitCode);
        Assert.StartsWith("Stockroom cannot start because the database was created or upgraded by a newer version of Stockroom.", result.StandardError, StringComparison.Ordinal);
        Assert.Contains(NewerMigration, result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", result.StandardError + result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANewerSchemaIsLeftUntouched()
    {
        var databaseUrl = await postgres.CreateDatabaseAsync(Token);
        await MarkAsMigratedByANewerVersionAsync(databaseUrl);
        await using var db = CreateContext(databaseUrl);

        var ex = await Assert.ThrowsAsync<DatabaseSchemaTooNewException>(() => DatabaseMigrator.MigrateAsync(db, Token));

        Assert.Equal([NewerMigration], ex.UnknownMigrations);
        Assert.Equal([NewerMigration], await db.Database.GetAppliedMigrationsAsync(Token));
    }

    /// <summary>Migrates the database, then records a migration this build does not have.</summary>
    private static async Task MarkAsMigratedByANewerVersionAsync(string databaseUrl)
    {
        await using (var db = CreateContext(databaseUrl))
        {
            await db.Database.MigrateAsync(Token);
        }

        await using var connection = new NpgsqlConnection(databaseUrl);
        await connection.OpenAsync(Token);
        await using var insert = new NpgsqlCommand(
            "INSERT INTO \"__EFMigrationsHistory\" (migration_id, product_version) VALUES (@id, '99.0.0')", connection);
        insert.Parameters.AddWithValue("id", NewerMigration);
        await insert.ExecuteNonQueryAsync(Token);
    }

    private static async Task<bool> HistoryTableExistsAsync(StockroomDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT to_regclass('\"__EFMigrationsHistory\"') IS NOT NULL";
        return (bool)(await command.ExecuteScalarAsync(Token))!;
    }

    private static StockroomDbContext CreateContext(string databaseUrl)
    {
        var options = new DbContextOptionsBuilder<StockroomDbContext>();
        options.UseStockroomDatabase(databaseUrl);
        return new StockroomDbContext(options.Options);
    }
}
