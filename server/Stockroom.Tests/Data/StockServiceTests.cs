using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Stockroom.Core.Locations;
using Stockroom.Core.Products;
using Stockroom.Core.Settings;
using Stockroom.Core.Stock;
using Stockroom.Core.Users;
using Stockroom.Data;
using Stockroom.Data.Settings;
using Stockroom.Data.Stock;
using Stockroom.Tests.Api;
using Stockroom.Tests.Infrastructure;

namespace Stockroom.Tests.Data;

public sealed class StockServiceTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 8, 15, 0, TimeSpan.Zero);

    /// <summary>The default actor, saved to each test's database together with the product.</summary>
    private static readonly User Actor = TestUsers.New("actor");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheFirstReceiveCreatesTheStockLevel()
    {
        var (databaseUrl, product) = await CreateDatabaseWithProductAsync();

        var movement = await ReceiveAsync(databaseUrl, new ReceiveStock(product.Id, 12, Actor.Id));

        Assert.Equal(12, movement.Delta);
        Assert.Equal(12, movement.QuantityAfter);
        Assert.Equal(12, await QuantityAsync(databaseUrl, product));
    }

    [Fact]
    public async Task AReceiveAddsToAnExistingStockLevel()
    {
        var (databaseUrl, product) = await CreateDatabaseWithProductAsync();
        await ReceiveAsync(databaseUrl, new ReceiveStock(product.Id, 12, Actor.Id));

        var movement = await ReceiveAsync(databaseUrl, new ReceiveStock(product.Id, 5, Actor.Id));

        Assert.Equal(5, movement.Delta);
        Assert.Equal(17, movement.QuantityAfter);
        Assert.Equal(17, await QuantityAsync(databaseUrl, product));
    }

    [Fact]
    public async Task TheReceiveMovementIsRecordedWithEveryField()
    {
        var (databaseUrl, product) = await CreateDatabaseWithProductAsync();
        var request = new ReceiveStock(product.Id, 3, Actor.Id)
        {
            Reason = StockMovementReason.Return,
            Note = "Customer changed their mind",
            Reference = "RMA-0042",
        };

        var movement = await ReceiveAsync(databaseUrl, request);

        await using var db = TestDatabase.CreateContext(databaseUrl);
        var stored = await db.StockMovements.SingleAsync(Token);
        Assert.Equivalent(movement, stored, strict: true);
        Assert.Equivalent(
            new
            {
                ProductId = product.Id,
                LocationId = Location.MainStorageId,
                Type = StockMovementType.Receive,
                Delta = 3,
                QuantityAfter = 3,
                Reason = StockMovementReason.Return,
                Note = "Customer changed their mind",
                Reference = "RMA-0042",
                VoidsMovementId = (Guid?)null,
                IdempotencyKey = (string?)null,
                ActorId = Actor.Id,
                CreatedAt = Now,
            },
            stored);
        Assert.Equal(7, stored.Id.Version);
        Assert.Equal(4, stored.PublicId.Version);
    }

    [Fact]
    public async Task TheReceiveReasonDefaultsToPurchase()
    {
        var (databaseUrl, product) = await CreateDatabaseWithProductAsync();

        var movement = await ReceiveAsync(databaseUrl, new ReceiveStock(product.Id, 1, Actor.Id));

        Assert.Equal(StockMovementReason.Purchase, movement.Reason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public async Task AReceiveQuantityThatIsNotPositiveIsRejected(int quantity)
    {
        var (databaseUrl, product) = await CreateDatabaseWithProductAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => ReceiveAsync(databaseUrl, new ReceiveStock(product.Id, quantity, Actor.Id)));

        await AssertNothingWrittenAsync(databaseUrl);
    }

    [Fact]
    public async Task AFailedMovementLeavesTheLevelUnchanged()
    {
        var (databaseUrl, product) = await CreateDatabaseWithProductAsync();
        await ReceiveAsync(databaseUrl, new ReceiveStock(product.Id, 12, Actor.Id));
        var unknownActor = TestDatabase.Ids.NewInternalId();

        // The level is updated before the movement is inserted, which then fails on the actor's foreign key.
        await Assert.ThrowsAsync<DbUpdateException>(
            () => ReceiveAsync(databaseUrl, new ReceiveStock(product.Id, 5, unknownActor)));

        Assert.Equal(12, await QuantityAsync(databaseUrl, product));
        await using var db = TestDatabase.CreateContext(databaseUrl);
        Assert.Equal(1, await db.StockMovements.CountAsync(Token));
    }

    [Fact]
    public async Task AReceiveInTheCallersTransactionIsUndoneWithIt()
    {
        var (databaseUrl, product) = await CreateDatabaseWithProductAsync();
        await using var db = TestDatabase.CreateContext(databaseUrl);

        await using (var transaction = await db.Database.BeginTransactionAsync(Token))
        {
            await Service(db).ReceiveAsync(new ReceiveStock(product.Id, 12, Actor.Id), Token);
            await transaction.RollbackAsync(Token);
        }

        await AssertNothingWrittenAsync(databaseUrl);
    }

    [Fact]
    public async Task ConcurrentReceivesAreAllCounted()
    {
        const int Receives = 20;
        var (databaseUrl, product) = await CreateDatabaseWithProductAsync();

        // Each receive has its own context and connection, as concurrent requests would, and all of them
        // race to create the level row.
        var movements = await Task.WhenAll(Enumerable.Range(0, Receives).Select(_ => Task.Run(
            () => ReceiveAsync(databaseUrl, new ReceiveStock(product.Id, 2, Actor.Id)),
            Token)));

        Assert.Equal(2 * Receives, await QuantityAsync(databaseUrl, product));
        Assert.Equal(
            Enumerable.Range(1, Receives).Select(n => 2 * n),
            movements.Select(m => m.QuantityAfter).Order());
    }

    [Fact]
    public async Task AnIssueRemovesStock()
    {
        var (databaseUrl, product) = await CreateDatabaseWithProductAsync();
        await ReceiveAsync(databaseUrl, new ReceiveStock(product.Id, 12, Actor.Id));
        var request = new IssueStock(product.Id, 5, Actor.Id)
        {
            Reason = StockMovementReason.Damaged,
            Note = "Dropped",
            Reference = "INC-7",
        };

        var movement = await IssueAsync(databaseUrl, request);

        Assert.Equal(7, await QuantityAsync(databaseUrl, product));
        await using var db = TestDatabase.CreateContext(databaseUrl);
        var stored = await db.StockMovements.SingleAsync(m => m.PublicId == movement.PublicId, Token);
        Assert.Equivalent(movement, stored, strict: true);
        Assert.Equivalent(
            new
            {
                ProductId = product.Id,
                LocationId = Location.MainStorageId,
                Type = StockMovementType.Issue,
                Delta = -5,
                QuantityAfter = 7,
                Reason = StockMovementReason.Damaged,
                Note = "Dropped",
                Reference = "INC-7",
                ActorId = Actor.Id,
                CreatedAt = Now,
            },
            stored);
    }

    [Fact]
    public async Task AnIssueMayTakeTheLastUnit()
    {
        var (databaseUrl, product) = await CreateDatabaseWithProductAsync();
        await ReceiveAsync(databaseUrl, new ReceiveStock(product.Id, 3, Actor.Id));

        var movement = await IssueAsync(databaseUrl, new IssueStock(product.Id, 3, Actor.Id));

        Assert.Equal(0, movement.QuantityAfter);
        Assert.Equal(0, await QuantityAsync(databaseUrl, product));
    }

    [Fact]
    public async Task TheIssueReasonDefaultsToSale()
    {
        var (databaseUrl, product) = await CreateDatabaseWithProductAsync();
        await ReceiveAsync(databaseUrl, new ReceiveStock(product.Id, 1, Actor.Id));

        var movement = await IssueAsync(databaseUrl, new IssueStock(product.Id, 1, Actor.Id));

        Assert.Equal(StockMovementReason.Sale, movement.Reason);
    }

    [Fact]
    public async Task IssuingMoreThanIsOnHandIsRejected()
    {
        var (databaseUrl, product) = await CreateDatabaseWithProductAsync();
        await ReceiveAsync(databaseUrl, new ReceiveStock(product.Id, 3, Actor.Id));

        var ex = await Assert.ThrowsAsync<InsufficientStockException>(
            () => IssueAsync(databaseUrl, new IssueStock(product.Id, 4, Actor.Id)));

        Assert.Equal((4, 3), (ex.Requested, ex.Available));
        Assert.Equal(3, await QuantityAsync(databaseUrl, product));
        await using var db = TestDatabase.CreateContext(databaseUrl);
        Assert.Equal(1, await db.StockMovements.CountAsync(Token));
    }

    [Fact]
    public async Task IssuingAProductThatNeverHadStockIsRejected()
    {
        var (databaseUrl, product) = await CreateDatabaseWithProductAsync();

        var ex = await Assert.ThrowsAsync<InsufficientStockException>(
            () => IssueAsync(databaseUrl, new IssueStock(product.Id, 1, Actor.Id)));

        Assert.Equal((1, 0), (ex.Requested, ex.Available));
        await AssertNothingWrittenAsync(databaseUrl);
    }

    [Fact]
    public async Task IssuingMoreThanIsOnHandIsRejectedWhenNegativeStockIsExplicitlyNotAllowed()
    {
        var (databaseUrl, product) = await CreateDatabaseWithProductAsync();
        await SetAllowNegativeStockAsync(databaseUrl, false);
        await ReceiveAsync(databaseUrl, new ReceiveStock(product.Id, 3, Actor.Id));

        await Assert.ThrowsAsync<InsufficientStockException>(
            () => IssueAsync(databaseUrl, new IssueStock(product.Id, 4, Actor.Id)));

        Assert.Equal(3, await QuantityAsync(databaseUrl, product));
    }

    [Fact]
    public async Task AnIssueMayGoBelowZeroWhenNegativeStockIsAllowed()
    {
        var (databaseUrl, product) = await CreateDatabaseWithProductAsync();
        await SetAllowNegativeStockAsync(databaseUrl, true);
        await ReceiveAsync(databaseUrl, new ReceiveStock(product.Id, 3, Actor.Id));

        var movement = await IssueAsync(databaseUrl, new IssueStock(product.Id, 5, Actor.Id));

        Assert.Equal(-5, movement.Delta);
        Assert.Equal(-2, movement.QuantityAfter);
        Assert.Equal(-2, await QuantityAsync(databaseUrl, product));
    }

    [Fact]
    public async Task AProductThatNeverHadStockMayGoBelowZeroWhenNegativeStockIsAllowed()
    {
        var (databaseUrl, product) = await CreateDatabaseWithProductAsync();
        await SetAllowNegativeStockAsync(databaseUrl, true);

        var movement = await IssueAsync(databaseUrl, new IssueStock(product.Id, 2, Actor.Id));

        Assert.Equal(-2, movement.QuantityAfter);
        Assert.Equal(-2, await QuantityAsync(databaseUrl, product));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public async Task AnIssueQuantityThatIsNotPositiveIsRejected(int quantity)
    {
        var (databaseUrl, product) = await CreateDatabaseWithProductAsync();
        await ReceiveAsync(databaseUrl, new ReceiveStock(product.Id, 3, Actor.Id));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => IssueAsync(databaseUrl, new IssueStock(product.Id, quantity, Actor.Id)));

        Assert.Equal(3, await QuantityAsync(databaseUrl, product));
    }

    [Fact]
    public async Task AnAdjustSetsTheCountedQuantity()
    {
        var (databaseUrl, product) = await CreateDatabaseWithProductAsync();
        await ReceiveAsync(databaseUrl, new ReceiveStock(product.Id, 45, Actor.Id));
        var request = new AdjustStock(product.Id, 42, Actor.Id)
        {
            ExpectedCurrent = 45,
            Note = "Three missing",
            Reference = "COUNT-2026-10",
        };

        var movement = await AdjustAsync(databaseUrl, request);

        Assert.NotNull(movement);
        Assert.Equal(42, await QuantityAsync(databaseUrl, product));
        await using var db = TestDatabase.CreateContext(databaseUrl);
        var stored = await db.StockMovements.SingleAsync(m => m.PublicId == movement.PublicId, Token);
        Assert.Equivalent(movement, stored, strict: true);
        Assert.Equivalent(
            new
            {
                ProductId = product.Id,
                LocationId = Location.MainStorageId,
                Type = StockMovementType.Adjust,
                Delta = -3,
                QuantityAfter = 42,
                Reason = StockMovementReason.Count,
                Note = "Three missing",
                Reference = "COUNT-2026-10",
                ActorId = Actor.Id,
                CreatedAt = Now,
            },
            stored);
    }

    [Fact]
    public async Task AnAdjustMayRaiseTheQuantity()
    {
        var (databaseUrl, product) = await CreateDatabaseWithProductAsync();
        await ReceiveAsync(databaseUrl, new ReceiveStock(product.Id, 3, Actor.Id));

        var movement = await AdjustAsync(
            databaseUrl, new AdjustStock(product.Id, 10, Actor.Id) { Reason = StockMovementReason.Found });

        Assert.Equal((7, 10, StockMovementReason.Found), (movement!.Delta, movement.QuantityAfter, movement.Reason));
        Assert.Equal(10, await QuantityAsync(databaseUrl, product));
    }

    [Fact]
    public async Task AnAdjustMaySetTheQuantityToZero()
    {
        var (databaseUrl, product) = await CreateDatabaseWithProductAsync();
        await ReceiveAsync(databaseUrl, new ReceiveStock(product.Id, 3, Actor.Id));

        var movement = await AdjustAsync(databaseUrl, new AdjustStock(product.Id, 0, Actor.Id) { ExpectedCurrent = 3 });

        Assert.Equal((-3, 0), (movement!.Delta, movement.QuantityAfter));
        Assert.Equal(0, await QuantityAsync(databaseUrl, product));
    }

    [Fact]
    public async Task AProductThatNeverHadStockIsAdjustedFromZero()
    {
        var (databaseUrl, product) = await CreateDatabaseWithProductAsync();

        var movement = await AdjustAsync(databaseUrl, new AdjustStock(product.Id, 5, Actor.Id) { ExpectedCurrent = 0 });

        Assert.Equal((5, 5), (movement!.Delta, movement.QuantityAfter));
        Assert.Equal(5, await QuantityAsync(databaseUrl, product));
    }

    [Fact]
    public async Task AnAdjustToTheCurrentQuantityWritesNothing()
    {
        var (databaseUrl, product) = await CreateDatabaseWithProductAsync();
        await ReceiveAsync(databaseUrl, new ReceiveStock(product.Id, 5, Actor.Id));

        var movement = await AdjustAsync(databaseUrl, new AdjustStock(product.Id, 5, Actor.Id) { ExpectedCurrent = 5 });

        Assert.Null(movement);
        Assert.Equal(5, await QuantityAsync(databaseUrl, product));
        await using var db = TestDatabase.CreateContext(databaseUrl);
        Assert.Equal(1, await db.StockMovements.CountAsync(Token));
    }

    [Fact]
    public async Task AStaleExpectedCurrentIsAConflict()
    {
        var (databaseUrl, product) = await CreateDatabaseWithProductAsync();
        await ReceiveAsync(databaseUrl, new ReceiveStock(product.Id, 45, Actor.Id));

        // Someone else removes two units while the user is counting.
        await IssueAsync(databaseUrl, new IssueStock(product.Id, 2, Actor.Id));

        var ex = await Assert.ThrowsAsync<StockConflictException>(
            () => AdjustAsync(databaseUrl, new AdjustStock(product.Id, 42, Actor.Id) { ExpectedCurrent = 45 }));

        Assert.Equal((45, 43), (ex.Expected, ex.Current));
        Assert.Equal(43, await QuantityAsync(databaseUrl, product));
        await using var db = TestDatabase.CreateContext(databaseUrl);
        Assert.Equal(2, await db.StockMovements.CountAsync(Token));
    }

    [Fact]
    public async Task AStaleExpectedCurrentIsAConflictEvenWhenTheTargetIsAlreadyReached()
    {
        var (databaseUrl, product) = await CreateDatabaseWithProductAsync();
        await ReceiveAsync(databaseUrl, new ReceiveStock(product.Id, 42, Actor.Id));

        var ex = await Assert.ThrowsAsync<StockConflictException>(
            () => AdjustAsync(databaseUrl, new AdjustStock(product.Id, 42, Actor.Id) { ExpectedCurrent = 45 }));

        Assert.Equal((45, 42), (ex.Expected, ex.Current));
    }

    [Fact]
    public async Task WithoutAnExpectedCurrentTheQuantityIsNotChecked()
    {
        var (databaseUrl, product) = await CreateDatabaseWithProductAsync();
        await ReceiveAsync(databaseUrl, new ReceiveStock(product.Id, 45, Actor.Id));

        var movement = await AdjustAsync(databaseUrl, new AdjustStock(product.Id, 40, Actor.Id));

        Assert.Equal((-5, 40), (movement!.Delta, movement.QuantityAfter));
    }

    [Fact]
    public async Task ANegativeTargetQuantityIsRejected()
    {
        var (databaseUrl, product) = await CreateDatabaseWithProductAsync();
        await ReceiveAsync(databaseUrl, new ReceiveStock(product.Id, 3, Actor.Id));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => AdjustAsync(databaseUrl, new AdjustStock(product.Id, -1, Actor.Id)));

        Assert.Equal(3, await QuantityAsync(databaseUrl, product));
    }

    [Fact]
    public async Task AProductCreatedWithAStartingQuantityGetsAnInitialMovement()
    {
        var databaseUrl = await TestDatabase.CreateMigratedAsync(postgres, Token);
        await TestDatabase.AddAsync(databaseUrl, Actor);
        var product = TestProducts.New("SR-000001");

        StockMovement movement;
        await using (var db = TestDatabase.CreateContext(databaseUrl))
        await using (var transaction = await db.Database.BeginTransactionAsync(Token))
        {
            db.Products.Add(product);
            await db.SaveChangesAsync(Token);
            movement = await Service(db).RecordInitialAsync(new InitialStock(product.Id, 8, Actor.Id), Token);
            await transaction.CommitAsync(Token);
        }

        Assert.Equal(8, await QuantityAsync(databaseUrl, product));
        await using var check = TestDatabase.CreateContext(databaseUrl);
        var stored = await check.StockMovements.SingleAsync(Token);
        Assert.Equivalent(movement, stored, strict: true);
        Assert.Equivalent(
            new
            {
                ProductId = product.Id,
                LocationId = Location.MainStorageId,
                Type = StockMovementType.Initial,
                Delta = 8,
                QuantityAfter = 8,
                Reason = StockMovementReason.Count,
                ActorId = Actor.Id,
                CreatedAt = Now,
            },
            stored);
    }

    [Fact]
    public async Task AnInitialMovementIsUndoneWithTheProductCreation()
    {
        var databaseUrl = await TestDatabase.CreateMigratedAsync(postgres, Token);
        await TestDatabase.AddAsync(databaseUrl, Actor);
        var product = TestProducts.New("SR-000001");

        await using (var db = TestDatabase.CreateContext(databaseUrl))
        await using (var transaction = await db.Database.BeginTransactionAsync(Token))
        {
            db.Products.Add(product);
            await db.SaveChangesAsync(Token);
            await Service(db).RecordInitialAsync(new InitialStock(product.Id, 8, Actor.Id), Token);
            await transaction.RollbackAsync(Token);
        }

        await AssertNothingWrittenAsync(databaseUrl);
        await using var check = TestDatabase.CreateContext(databaseUrl);
        Assert.False(await check.Products.AnyAsync(Token));
    }

    [Fact]
    public async Task AProductWithStockHistoryCannotGetAnInitialMovement()
    {
        var (databaseUrl, product) = await CreateDatabaseWithProductAsync();
        await ReceiveAsync(databaseUrl, new ReceiveStock(product.Id, 3, Actor.Id));
        await IssueAsync(databaseUrl, new IssueStock(product.Id, 3, Actor.Id));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => RecordInitialAsync(databaseUrl, new InitialStock(product.Id, 5, Actor.Id)));

        Assert.Equal(0, await QuantityAsync(databaseUrl, product));
        await using var db = TestDatabase.CreateContext(databaseUrl);
        Assert.Equal(2, await db.StockMovements.CountAsync(Token));
    }

    [Fact]
    public async Task AProductGetsOnlyOneInitialMovement()
    {
        var (databaseUrl, product) = await CreateDatabaseWithProductAsync();
        await RecordInitialAsync(databaseUrl, new InitialStock(product.Id, 8, Actor.Id));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => RecordInitialAsync(databaseUrl, new InitialStock(product.Id, 8, Actor.Id)));

        Assert.Equal(8, await QuantityAsync(databaseUrl, product));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public async Task AnInitialQuantityThatIsNotPositiveIsRejected(int quantity)
    {
        var (databaseUrl, product) = await CreateDatabaseWithProductAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => RecordInitialAsync(databaseUrl, new InitialStock(product.Id, quantity, Actor.Id)));

        await AssertNothingWrittenAsync(databaseUrl);
    }

    [Fact]
    public async Task TheApiProvidesTheService()
    {
        await using var factory = await StockroomApiFactory.CreateAsync(postgres);
        await using var scope = factory.Services.CreateAsyncScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<StockService>());
    }

    private async Task<(string DatabaseUrl, Product Product)> CreateDatabaseWithProductAsync()
    {
        var databaseUrl = await TestDatabase.CreateMigratedAsync(postgres, Token);
        var product = TestProducts.New("SR-000001");
        await TestDatabase.AddAsync(databaseUrl, Actor, product);
        return (databaseUrl, product);
    }

    private static StockService Service(StockroomDbContext db) =>
        new(db, new SettingsStore(db), TestDatabase.Ids, new FakeTimeProvider(Now));

    private static async Task<StockMovement> RecordInitialAsync(string databaseUrl, InitialStock request)
    {
        await using var db = TestDatabase.CreateContext(databaseUrl);
        return await Service(db).RecordInitialAsync(request, Token);
    }

    private static async Task<StockMovement> ReceiveAsync(string databaseUrl, ReceiveStock request)
    {
        await using var db = TestDatabase.CreateContext(databaseUrl);
        return await Service(db).ReceiveAsync(request, Token);
    }

    private static async Task<StockMovement> IssueAsync(string databaseUrl, IssueStock request)
    {
        await using var db = TestDatabase.CreateContext(databaseUrl);
        return await Service(db).IssueAsync(request, Token);
    }

    private static async Task<StockMovement?> AdjustAsync(string databaseUrl, AdjustStock request)
    {
        await using var db = TestDatabase.CreateContext(databaseUrl);
        return await Service(db).AdjustAsync(request, Token);
    }

    private static async Task SetAllowNegativeStockAsync(string databaseUrl, bool allowed)
    {
        await using var db = TestDatabase.CreateContext(databaseUrl);
        await new SettingsStore(db).SetAsync(StockroomSettings.AllowNegativeStock, allowed, Token);
    }

    private static async Task<int> QuantityAsync(string databaseUrl, Product product)
    {
        await using var db = TestDatabase.CreateContext(databaseUrl);
        return await db.StockLevels
            .Where(l => l.ProductId == product.Id && l.LocationId == Location.MainStorageId)
            .Select(l => l.Quantity)
            .SingleAsync(Token);
    }

    private static async Task AssertNothingWrittenAsync(string databaseUrl)
    {
        await using var db = TestDatabase.CreateContext(databaseUrl);
        Assert.False(await db.StockLevels.AnyAsync(Token));
        Assert.False(await db.StockMovements.AnyAsync(Token));
    }
}
