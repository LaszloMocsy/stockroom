using Microsoft.EntityFrameworkCore;
using Stockroom.Core.Locations;
using Stockroom.Core.Products;
using Stockroom.Core.Stock;
using Stockroom.Core.Users;
using Stockroom.Data;
using Stockroom.Data.Settings;
using Stockroom.Data.Stock;
using Stockroom.Tests.Infrastructure;

namespace Stockroom.Tests.Data;

/// <summary>
/// Parallel issues against one product never oversell (spec 3.2, rule 3; spec 13). Every worker has its
/// own context and an open connection before a shared start signal releases them, so the calls really
/// race in PostgreSQL. The assertions hold for every interleaving, so the tests are deterministic.
/// </summary>
public sealed class StockConcurrencyTests(PostgresFixture postgres)
{
    private const int Workers = 40;

    private static readonly User Actor = TestUsers.New("actor");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ParallelIssuesTakeExactlyTheStockOnHand()
    {
        const int OnHand = 25;
        var (databaseUrl, product) = await CreateDatabaseWithStockAsync(OnHand);

        var outcomes = await IssueInParallelAsync(databaseUrl, Enumerable.Repeat(new IssueStock(product.Id, 1, Actor.Id), Workers));

        Assert.Equal(OnHand, outcomes.Count(o => o.Movement is not null));
        Assert.All(outcomes.Where(o => o.Movement is null), o => Assert.Equal((1, 0), (o.Refusal!.Requested, o.Refusal.Available)));
        Assert.Equal(
            Enumerable.Range(0, OnHand),
            outcomes.Where(o => o.Movement is not null).Select(o => o.Movement!.QuantityAfter).Order());
        await AssertLevelMatchesLedgerAsync(databaseUrl, product, expected: 0);
    }

    [Fact]
    public async Task ParallelIssuesOfMixedSizesNeverTakeStockBelowZero()
    {
        const int OnHand = 60;
        var (databaseUrl, product) = await CreateDatabaseWithStockAsync(OnHand);

        // 1 to 5 units each, 120 in total: about half can succeed, depending on the order they run in.
        var requests = Enumerable.Range(0, Workers).Select(i => new IssueStock(product.Id, (i % 5) + 1, Actor.Id));
        var outcomes = await IssueInParallelAsync(databaseUrl, requests);

        var issued = outcomes.Where(o => o.Movement is not null).Select(o => o.Movement!).ToList();
        var final = OnHand + issued.Sum(m => m.Delta);
        Assert.InRange(final, 0, OnHand);

        // Each issue started from the quantity the one before it left behind: none saw a stale level.
        var running = OnHand;
        foreach (var movement in issued.OrderByDescending(m => m.QuantityAfter))
        {
            running += movement.Delta;
            Assert.Equal(running, movement.QuantityAfter);
        }

        // A refused issue asked for more than was left when it ran, and stock only fell after that.
        Assert.All(outcomes.Where(o => o.Movement is null), o =>
        {
            Assert.True(o.Refusal!.Available < o.Refusal.Requested);
            Assert.True(final <= o.Refusal.Available);
        });

        await AssertLevelMatchesLedgerAsync(databaseUrl, product, expected: final);
    }

    private sealed record Outcome(StockMovement? Movement, InsufficientStockException? Refusal);

    private static async Task<Outcome[]> IssueInParallelAsync(string databaseUrl, IEnumerable<IssueStock> requests)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workers = requests.Select(request => Task.Run(async () =>
        {
            await using var db = TestDatabase.CreateContext(databaseUrl);
            await db.Database.OpenConnectionAsync(Token);
            var service = new StockService(db, new SettingsStore(db), TestDatabase.Ids, TimeProvider.System);
            await start.Task.WaitAsync(Token);

            try
            {
                return new Outcome(await service.IssueAsync(request, Token), null);
            }
            catch (InsufficientStockException ex)
            {
                return new Outcome(null, ex);
            }
        }, Token)).ToList();

        start.SetResult();
        return await Task.WhenAll(workers);
    }

    private async Task<(string DatabaseUrl, Product Product)> CreateDatabaseWithStockAsync(int quantity)
    {
        var databaseUrl = await TestDatabase.CreateMigratedAsync(postgres, Token);
        var product = TestProducts.New("SR-000001");
        await TestDatabase.AddAsync(databaseUrl, Actor, product);

        await using var db = TestDatabase.CreateContext(databaseUrl);
        await new StockService(db, new SettingsStore(db), TestDatabase.Ids, TimeProvider.System)
            .ReceiveAsync(new ReceiveStock(product.Id, quantity, Actor.Id), Token);
        return (databaseUrl, product);
    }

    private static async Task AssertLevelMatchesLedgerAsync(string databaseUrl, Product product, int expected)
    {
        await using var db = TestDatabase.CreateContext(databaseUrl);
        var level = await db.StockLevels
            .Where(l => l.ProductId == product.Id && l.LocationId == Location.MainStorageId)
            .Select(l => l.Quantity)
            .SingleAsync(Token);
        var ledger = await db.StockMovements.Where(m => m.ProductId == product.Id).SumAsync(m => m.Delta, Token);

        Assert.Equal(expected, level);
        Assert.Equal(expected, ledger);
    }
}
