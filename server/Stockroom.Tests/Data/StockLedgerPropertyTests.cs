using FsCheck;
using FsCheck.Fluent;
using Microsoft.EntityFrameworkCore;
using Stockroom.Core.Locations;
using Stockroom.Core.Products;
using Stockroom.Core.Settings;
using Stockroom.Core.Stock;
using Stockroom.Core.Users;
using Stockroom.Data;
using Stockroom.Data.Settings;
using Stockroom.Data.Stock;
using Stockroom.Tests.Infrastructure;

namespace Stockroom.Tests.Data;

/// <summary>
/// After any random sequence of stock operations, every cached level equals the sum of its ledger (spec 13).
/// Each sequence runs against <see cref="StockService"/> and against a small model of the rules in spec 3.2;
/// every operation must succeed or be refused exactly as the model predicts, with the quantity it predicts.
/// </summary>
/// <remarks>
/// FsCheck picks a new seed each run. A failure reports the seed and the shortest failing sequence it found
/// by dropping operations, so a run can be repeated with <c>Config.WithReplay</c>.
/// </remarks>
public sealed class StockLedgerPropertyTests(PostgresFixture postgres)
{
    private const int Products = 2;

    private static readonly User Actor = TestUsers.New("actor");

    [Fact]
    public async Task RandomOperationSequencesKeepLevelsEqualToTheLedger()
    {
        var databaseUrl = await TestDatabase.CreateMigratedAsync(postgres, TestContext.Current.CancellationToken);
        await TestDatabase.AddAsync(databaseUrl, Actor);

        var property = Prop.ForAll(Scenarios, scenario => new ScenarioRun(databaseUrl).RunAsync(scenario));

        Check.One(Config.QuickThrowOnFailure.WithMaxTest(50), property);
    }

    private static readonly Gen<int> ProductIndex = Gen.Choose(0, Products - 1);

    private static readonly Gen<Operation> Operations = Gen.Frequency(
        (4, from product in ProductIndex from quantity in Gen.Choose(1, 20) select (Operation)new Receive(product, quantity)),
        (4, from product in ProductIndex from quantity in Gen.Choose(1, 20) select (Operation)new Issue(product, quantity)),
        (2, from product in ProductIndex
            from target in Gen.Choose(0, 30)
            from expectation in Gen.Elements(Expectation.None, Expectation.Current, Expectation.Stale)
            select (Operation)new Adjust(product, target, expectation)),
        (2, Gen.Choose(0, 1000).Select(pick => (Operation)new Void(pick))),
        (1, Gen.Choose(0, 1000).Select(pick => (Operation)new Retry(pick))),
        (1, Gen.Elements(true, false).Select(allowed => (Operation)new SetAllowNegativeStock(allowed))));

    private static readonly Arbitrary<Scenario> Scenarios = Arb.From(
        from initial in Gen.Choose(0, 10).ArrayOf(Products)
        from length in Gen.Choose(1, 40)
        from operations in Operations.ArrayOf(length)
        select new Scenario(initial, operations),
        // Shrink by dropping one operation at a time; picks wrap around, so every shorter sequence is valid.
        scenario => scenario.Operations.Select((_, skipped) => scenario with
        {
            Operations = scenario.Operations.Where((_, i) => i != skipped).ToArray(),
        }));

    /// <param name="Initial">Starting quantity of each product; 0 means it is created without stock.</param>
    private sealed record Scenario(int[] Initial, Operation[] Operations)
    {
        public override string ToString() =>
            $"Initial [{string.Join(", ", Initial)}], operations:{string.Concat(Operations.Select(o => $"{Environment.NewLine}  {o}"))}";
    }

    private abstract record Operation;

    private sealed record Receive(int Product, int Quantity) : Operation;

    private sealed record Issue(int Product, int Quantity) : Operation;

    private sealed record Adjust(int Product, int Target, Expectation Expectation) : Operation;

    /// <summary>Voids one of the movements made so far, chosen by <paramref name="Pick"/> modulo their number.</summary>
    private sealed record Void(int Pick) : Operation;

    /// <summary>Repeats one of the successful requests made so far, with the same idempotency key.</summary>
    private sealed record Retry(int Pick) : Operation;

    private sealed record SetAllowNegativeStock(bool Allowed) : Operation;

    /// <summary>What an adjust passes as <c>expected_current</c>: nothing, the true quantity, or a stale one.</summary>
    private enum Expectation
    {
        None,
        Current,
        Stale,
    }

    /// <summary>One scenario: fresh products in the shared database, and the model of their stock.</summary>
    private sealed class ScenarioRun(string databaseUrl)
    {
        private readonly CancellationToken _token = TestContext.Current.CancellationToken;
        private readonly Product[] _products = Enumerable.Range(0, Products).Select(_ => TestProducts.New($"P-{Guid.NewGuid():N}")).ToArray();
        private readonly int[] _quantities = new int[Products];
        private readonly List<StockMovement> _movements = [];
        private readonly HashSet<Guid> _voided = [];
        private readonly List<(Func<StockService, Task<StockMovement?>> Send, StockMovement Movement)> _sent = [];
        private bool _allowNegativeStock;

        public async Task RunAsync(Scenario scenario)
        {
            await CreateProductsAsync(scenario.Initial);
            await using (var db = TestDatabase.CreateContext(databaseUrl))
            {
                await new SettingsStore(db).SetAsync(StockroomSettings.AllowNegativeStock, false, _token);
            }

            foreach (var operation in scenario.Operations)
            {
                await RunAsync(operation);
            }

            await AssertLevelsMatchModelAndLedgerAsync();
        }

        private async Task CreateProductsAsync(int[] initial)
        {
            for (var i = 0; i < Products; i++)
            {
                await using var db = TestDatabase.CreateContext(databaseUrl);
                await using var transaction = await db.Database.BeginTransactionAsync(_token);
                db.Products.Add(_products[i]);
                await db.SaveChangesAsync(_token);
                if (initial[i] > 0)
                {
                    _movements.Add(await Service(db).RecordInitialAsync(new InitialStock(_products[i].Id, initial[i], Actor.Id), _token));
                    _quantities[i] = initial[i];
                }

                await transaction.CommitAsync(_token);
            }
        }

        private Task RunAsync(Operation operation) => operation switch
        {
            Receive receive => ReceiveAsync(receive),
            Issue issue => IssueAsync(issue),
            Adjust adjust => AdjustAsync(adjust),
            Void @void => VoidAsync(@void),
            Retry retry => RetryAsync(retry),
            SetAllowNegativeStock set => SetAllowNegativeStockAsync(set.Allowed),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
        };

        private async Task ReceiveAsync(Receive operation)
        {
            var request = new ReceiveStock(_products[operation.Product].Id, operation.Quantity, Actor.Id) { IdempotencyKey = NewKey() };

            await ExpectMovementAsync(async s => await s.ReceiveAsync(request, _token), operation.Product, operation.Quantity);
        }

        private async Task IssueAsync(Issue operation)
        {
            var request = new IssueStock(_products[operation.Product].Id, operation.Quantity, Actor.Id) { IdempotencyKey = NewKey() };
            var current = _quantities[operation.Product];

            if (current < operation.Quantity && !_allowNegativeStock)
            {
                var ex = await ExpectRefusalAsync<InsufficientStockException>(s => s.IssueAsync(request, _token));
                Assert.Equal((operation.Quantity, current), (ex.Requested, ex.Available));
                return;
            }

            await ExpectMovementAsync(async s => await s.IssueAsync(request, _token), operation.Product, -operation.Quantity);
        }

        private async Task AdjustAsync(Adjust operation)
        {
            var current = _quantities[operation.Product];
            var request = new AdjustStock(_products[operation.Product].Id, operation.Target, Actor.Id)
            {
                ExpectedCurrent = operation.Expectation switch
                {
                    Expectation.Current => current,
                    Expectation.Stale => current + 1,
                    _ => null,
                },
                IdempotencyKey = NewKey(),
            };

            if (operation.Expectation == Expectation.Stale)
            {
                var ex = await ExpectRefusalAsync<StockConflictException>(s => s.AdjustAsync(request, _token));
                Assert.Equal((current + 1, current), (ex.Expected, ex.Current));
                return;
            }

            if (operation.Target == current)
            {
                await using var db = TestDatabase.CreateContext(databaseUrl);
                Assert.Null(await Service(db).AdjustAsync(request, _token));
                return;
            }

            await ExpectMovementAsync(s => s.AdjustAsync(request, _token), operation.Product, operation.Target - current);
        }

        private async Task VoidAsync(Void operation)
        {
            if (_movements.Count == 0)
            {
                return;
            }

            var target = _movements[operation.Pick % _movements.Count];
            var product = Array.FindIndex(_products, p => p.Id == target.ProductId);
            var request = new VoidStock(target.Id, Actor.Id) { IdempotencyKey = NewKey() };
            var current = _quantities[product];

            if (target.Type == StockMovementType.Void || _voided.Contains(target.Id))
            {
                var ex = await ExpectRefusalAsync<MovementNotVoidableException>(s => s.VoidAsync(request, _token));
                Assert.Equal(
                    target.Type == StockMovementType.Void ? MovementNotVoidableReason.IsVoid : MovementNotVoidableReason.AlreadyVoided,
                    ex.Reason);
                return;
            }

            if (current - target.Delta < 0 && target.Delta > 0 && !_allowNegativeStock)
            {
                var ex = await ExpectRefusalAsync<InsufficientStockException>(s => s.VoidAsync(request, _token));
                Assert.Equal((target.Delta, current), (ex.Requested, ex.Available));
                return;
            }

            await ExpectMovementAsync(async s => await s.VoidAsync(request, _token), product, -target.Delta);
            _voided.Add(target.Id);
        }

        private async Task RetryAsync(Retry operation)
        {
            if (_sent.Count == 0)
            {
                return;
            }

            var (send, original) = _sent[operation.Pick % _sent.Count];
            await using var db = TestDatabase.CreateContext(databaseUrl);

            Assert.Equivalent(original, await send(Service(db)), strict: true);
        }

        private async Task SetAllowNegativeStockAsync(bool allowed)
        {
            await using var db = TestDatabase.CreateContext(databaseUrl);
            await new SettingsStore(db).SetAsync(StockroomSettings.AllowNegativeStock, allowed, _token);
            _allowNegativeStock = allowed;
        }

        /// <summary>Sends the request, which the model says changes the product's quantity by <paramref name="delta"/>.</summary>
        private async Task ExpectMovementAsync(Func<StockService, Task<StockMovement?>> send, int product, int delta)
        {
            await using var db = TestDatabase.CreateContext(databaseUrl);
            var movement = await send(Service(db));

            _quantities[product] += delta;
            Assert.NotNull(movement);
            Assert.Equal((delta, _quantities[product]), (movement.Delta, movement.QuantityAfter));
            _movements.Add(movement);
            _sent.Add((send, movement));
        }

        private async Task<TException> ExpectRefusalAsync<TException>(Func<StockService, Task> send)
            where TException : Exception
        {
            await using var db = TestDatabase.CreateContext(databaseUrl);
            return await Assert.ThrowsAsync<TException>(() => send(Service(db)));
        }

        private async Task AssertLevelsMatchModelAndLedgerAsync()
        {
            await using var db = TestDatabase.CreateContext(databaseUrl);
            for (var i = 0; i < Products; i++)
            {
                var productId = _products[i].Id;
                var level = await db.StockLevels
                    .Where(l => l.ProductId == productId && l.LocationId == Location.MainStorageId)
                    .Select(l => (int?)l.Quantity)
                    .SingleOrDefaultAsync(_token) ?? 0;
                var ledger = await db.StockMovements.Where(m => m.ProductId == productId).SumAsync(m => m.Delta, _token);

                Assert.Equal((_quantities[i], _quantities[i]), (level, ledger));
            }

            // Only this scenario's products: earlier scenarios share the database, and while shrinking a
            // failure, their drift would make every shorter sequence look like it fails too.
            var drift = await new StockReconciler(db).FindDriftAsync(_token);
            Assert.DoesNotContain(drift, d => _products.Any(p => p.Id == d.ProductId));
        }

        private static string NewKey() => Guid.NewGuid().ToString("N");

        private static StockService Service(StockroomDbContext db) =>
            new(db, new SettingsStore(db), TestDatabase.Ids, TimeProvider.System);
    }
}
