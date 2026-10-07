using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Stockroom.Core.Settings;
using Stockroom.Data.Settings;
using Stockroom.Tests.Api;
using Stockroom.Tests.Infrastructure;

namespace Stockroom.Tests.Data;

public sealed class SettingsStoreTests(PostgresFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NegativeStockIsNotAllowedByDefault()
    {
        var databaseUrl = await TestDatabase.CreateMigratedAsync(postgres, Token);
        await using var db = TestDatabase.CreateContext(databaseUrl);

        Assert.False(await new SettingsStore(db).GetAsync(StockroomSettings.AllowNegativeStock, Token));
    }

    [Fact]
    public async Task AWrittenValueIsReadBack()
    {
        var databaseUrl = await TestDatabase.CreateMigratedAsync(postgres, Token);
        await using (var db = TestDatabase.CreateContext(databaseUrl))
        {
            await new SettingsStore(db).SetAsync(StockroomSettings.AllowNegativeStock, true, Token);
        }

        await using (var db = TestDatabase.CreateContext(databaseUrl))
        {
            Assert.True(await new SettingsStore(db).GetAsync(StockroomSettings.AllowNegativeStock, Token));
        }
    }

    [Fact]
    public async Task WritingAgainReplacesTheValue()
    {
        var databaseUrl = await TestDatabase.CreateMigratedAsync(postgres, Token);
        await using var db = TestDatabase.CreateContext(databaseUrl);
        var store = new SettingsStore(db);

        await store.SetAsync(StockroomSettings.AllowNegativeStock, true, Token);
        await store.SetAsync(StockroomSettings.AllowNegativeStock, false, Token);

        Assert.False(await store.GetAsync(StockroomSettings.AllowNegativeStock, Token));
        Assert.Equal(1, await db.Settings.CountAsync(Token));
    }

    [Fact]
    public async Task ValuesAreStoredAsJsonUnderTheirKey()
    {
        var databaseUrl = await TestDatabase.CreateMigratedAsync(postgres, Token);
        await using (var db = TestDatabase.CreateContext(databaseUrl))
        {
            await new SettingsStore(db).SetAsync(StockroomSettings.AllowNegativeStock, true, Token);
        }

        await using var connection = new NpgsqlConnection(databaseUrl);
        await connection.OpenAsync(Token);
        await using var query = new NpgsqlCommand("SELECT key || '=' || value::text FROM settings", connection);

        Assert.Equal("allow_negative_stock=true", await query.ExecuteScalarAsync(Token));
    }

    [Fact]
    public async Task AWriteIsUndoneWithItsTransaction()
    {
        var databaseUrl = await TestDatabase.CreateMigratedAsync(postgres, Token);
        await using var db = TestDatabase.CreateContext(databaseUrl);
        var store = new SettingsStore(db);

        await using (var transaction = await db.Database.BeginTransactionAsync(Token))
        {
            await store.SetAsync(StockroomSettings.AllowNegativeStock, true, Token);
            Assert.True(await store.GetAsync(StockroomSettings.AllowNegativeStock, Token));
            await transaction.RollbackAsync(Token);
        }

        Assert.False(await store.GetAsync(StockroomSettings.AllowNegativeStock, Token));
    }

    [Fact]
    public async Task TheApiProvidesTheStore()
    {
        await using var factory = await StockroomApiFactory.CreateAsync(postgres);
        await using var scope = factory.Services.CreateAsyncScope();

        var store = scope.ServiceProvider.GetRequiredService<ISettingsStore>();

        Assert.IsType<SettingsStore>(store);
        Assert.False(await store.GetAsync(StockroomSettings.AllowNegativeStock, Token));
    }
}
