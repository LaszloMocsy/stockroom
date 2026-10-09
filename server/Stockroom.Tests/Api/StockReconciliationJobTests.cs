using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stockroom.Api.Configuration;
using Stockroom.Core.Locations;
using Stockroom.Core.Products;
using Stockroom.Core.Stock;
using Stockroom.Tests.Data;
using Stockroom.Tests.Infrastructure;

namespace Stockroom.Tests.Api;

/// <summary>
/// The scheduled reconciliation, on a one-second interval so a test sees several runs. Each test waits for
/// log entries written after a mark, so they come from runs that saw the change made before it.
/// </summary>
public sealed class StockReconciliationJobTests(PostgresFixture postgres)
{
    private const string JobCategory = "Stockroom.Api.Jobs.StockReconciliationJob";
    private const int NoDrift = 1;
    private const int DriftFound = 2;
    private const int RunFailed = 3;
    private const int Disabled = 4;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EveryRunLogsTheDriftItFinds()
    {
        await using var factory = await CreateFactoryAsync(intervalSeconds: "1");
        var logs = new CapturingLoggerProvider();
        await using var app = Start(factory, logs);
        var databaseUrl = factory.Settings[StockroomOptions.DatabaseUrlKey]!;

        // The run at startup finds the new database consistent.
        await WaitUntilAsync(logs, entries => entries.Any(e => e.EventId.Id == NoDrift));

        // A level of 5 with no movements behind it.
        var product = TestProducts.New($"P-{Guid.NewGuid():N}");
        await TestDatabase.AddAsync(
            databaseUrl, product, new StockLevel { ProductId = product.Id, LocationId = Location.MainStorageId, Quantity = 5 });

        // Two runs, so it repeats on its interval.
        var entries = await WaitUntilAsync(logs, entries => DriftOf(entries, product).Count() >= 2);
        Assert.All(DriftOf(entries, product), entry =>
        {
            Assert.Equal(LogLevel.Warning, entry.Level);
            Assert.Contains("the level is 5 but the ledger sums to 0 (difference 5)", entry.Message, StringComparison.Ordinal);
        });

        // Once the level is repaired, later runs find nothing again.
        await using (var db = TestDatabase.CreateContext(databaseUrl))
        {
            await db.StockLevels.Where(l => l.ProductId == product.Id).ExecuteDeleteAsync(Token);
        }

        var repaired = JobEntries(logs).Count;
        await WaitUntilAsync(logs, entries => entries.Skip(repaired).Any(e => e.EventId.Id == NoDrift));
    }

    [Fact]
    public async Task AFailedRunDoesNotStopTheJob()
    {
        await using var factory = await CreateFactoryAsync(intervalSeconds: "1");
        var logs = new CapturingLoggerProvider();
        await using var app = Start(factory, logs);
        await WaitUntilAsync(logs, entries => entries.Any(e => e.EventId.Id == NoDrift));

        // Runs fail while a table they read is missing.
        await using var db = TestDatabase.CreateContext(factory.Settings[StockroomOptions.DatabaseUrlKey]!);
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE stock_levels RENAME TO stock_levels_hidden", Token);
        var hidden = JobEntries(logs).Count;
        var entries = await WaitUntilAsync(logs, entries => entries.Skip(hidden).Any(e => e.EventId.Id == RunFailed));
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE stock_levels_hidden RENAME TO stock_levels", Token);
        var restored = JobEntries(logs).Count;

        await WaitUntilAsync(logs, entries => entries.Skip(restored).Any(e => e.EventId.Id == NoDrift));
        var failure = entries.Skip(hidden).First(e => e.EventId.Id == RunFailed);
        Assert.Equal(LogLevel.Error, failure.Level);
        Assert.IsType<Npgsql.PostgresException>(failure.Exception);
    }

    [Fact]
    public async Task AZeroIntervalTurnsTheJobOff()
    {
        await using var factory = await CreateFactoryAsync(intervalSeconds: "0");
        var logs = new CapturingLoggerProvider();
        await using var app = Start(factory, logs);

        var entries = await WaitUntilAsync(logs, entries => entries.Count > 0);

        Assert.Equal(Disabled, Assert.Single(entries).EventId.Id);
    }

    private Task<StockroomApiFactory> CreateFactoryAsync(string intervalSeconds) =>
        StockroomApiFactory.CreateAsync(postgres, configure: settings =>
            settings[StockroomOptions.ReconciliationIntervalSecondsKey] = intervalSeconds);

    /// <summary>Starts the API, and with it the job, capturing its logs in <paramref name="logs"/>.</summary>
    private static WebApplicationFactory<Program> Start(StockroomApiFactory factory, CapturingLoggerProvider logs)
    {
        var app = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<ILoggerProvider>(logs)));
        _ = app.Server;
        return app;
    }

    private static List<CapturedLog> JobEntries(CapturingLoggerProvider logs) =>
        logs.Entries.Where(e => e.Category == JobCategory).ToList();

    private static IEnumerable<CapturedLog> DriftOf(IEnumerable<CapturedLog> entries, Product product) =>
        entries.Where(e => e.EventId.Id == DriftFound && e.Message.Contains(product.PublicId.ToString(), StringComparison.Ordinal));

    /// <summary>Polls the job's log entries until <paramref name="done"/> holds, and returns them.</summary>
    private static async Task<List<CapturedLog>> WaitUntilAsync(CapturingLoggerProvider logs, Func<List<CapturedLog>, bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var entries = JobEntries(logs);
            if (done(entries))
            {
                return entries;
            }

            Assert.True(DateTime.UtcNow < deadline, "The reconciliation job did not log what was expected in time.");
            await Task.Delay(TimeSpan.FromMilliseconds(50), Token);
        }
    }
}
