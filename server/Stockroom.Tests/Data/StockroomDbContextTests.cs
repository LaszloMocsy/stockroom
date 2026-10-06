using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stockroom.Api.Configuration;
using Stockroom.Data;
using Stockroom.Tests.Api;
using Stockroom.Tests.Infrastructure;

namespace Stockroom.Tests.Data;

public sealed class StockroomDbContextTests(PostgresFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheApiContextConnectsToTheConfiguredDatabase()
    {
        var connectionString = await postgres.CreateDatabaseAsync(Token);
        var settings = new Dictionary<string, string?>(StockroomApiFactory.ValidSettings)
        {
            [StockroomOptions.DatabaseUrlKey] = connectionString,
        };
        await using var factory = StockroomApiFactory.WithSettings(settings);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StockroomDbContext>();

        Assert.True(db.Database.IsNpgsql());
        Assert.True(await db.Database.CanConnectAsync(Token));
        Assert.Equal(connectionString, db.Database.GetConnectionString());
    }
}
