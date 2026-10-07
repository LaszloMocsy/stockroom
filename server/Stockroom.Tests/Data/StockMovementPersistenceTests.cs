using Microsoft.EntityFrameworkCore;
using Npgsql;
using Stockroom.Core.Locations;
using Stockroom.Core.Products;
using Stockroom.Core.Stock;
using Stockroom.Tests.Infrastructure;

namespace Stockroom.Tests.Data;

public sealed class StockMovementPersistenceTests(PostgresFixture postgres)
{
    private static readonly Guid Actor = TestDatabase.Ids.NewInternalId();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AMovementIsPersistedWithEveryField()
    {
        var (databaseUrl, product) = await CreateDatabaseWithProductAsync();
        var received = Movement(product, StockMovementType.Receive, delta: 10, quantityAfter: 10);
        var voided = new StockMovement
        {
            Id = TestDatabase.Ids.NewInternalId(),
            PublicId = TestDatabase.Ids.NewPublicId(),
            ProductId = product.Id,
            LocationId = Location.MainStorageId,
            Type = StockMovementType.Void,
            Delta = -10,
            QuantityAfter = 0,
            Reason = StockMovementReason.Correction,
            Note = "Entered twice",
            Reference = "PO-2026-0042",
            VoidsMovementId = received.Id,
            IdempotencyKey = "7c1b0d2e",
            ActorId = Actor,
            CreatedAt = new DateTimeOffset(2026, 10, 7, 9, 30, 0, TimeSpan.Zero),
        };
        await TestDatabase.AddAsync(databaseUrl, received);
        await TestDatabase.AddAsync(databaseUrl, voided);

        await using var db = TestDatabase.CreateContext(databaseUrl);
        var loaded = await db.StockMovements.SingleAsync(m => m.PublicId == voided.PublicId, Token);

        Assert.Equivalent(voided, loaded, strict: true);
    }

    [Fact]
    public async Task EnumsAreStoredAsSnakeCaseNames()
    {
        var (databaseUrl, product) = await CreateDatabaseWithProductAsync();
        await TestDatabase.AddAsync(databaseUrl, Movement(product, StockMovementType.Receive, delta: 5, quantityAfter: 5));

        await using var connection = new NpgsqlConnection(databaseUrl);
        await connection.OpenAsync(Token);
        await using var query = new NpgsqlCommand("SELECT type || ',' || reason FROM stock_movements", connection);

        Assert.Equal("receive,purchase", await query.ExecuteScalarAsync(Token));
    }

    [Fact]
    public async Task AnIdempotencyKeyIsUniquePerActor()
    {
        var (databaseUrl, product) = await CreateDatabaseWithProductAsync();
        await TestDatabase.AddAsync(databaseUrl, Movement(product, idempotencyKey: "retry-me"));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(
            () => TestDatabase.AddAsync(databaseUrl, Movement(product, idempotencyKey: "retry-me")));

        TestDatabase.AssertConstraintViolation(ex, PostgresErrorCodes.UniqueViolation, "ix_stock_movements_actor_id_idempotency_key");
    }

    [Fact]
    public async Task DifferentActorsMayUseTheSameIdempotencyKey()
    {
        var (databaseUrl, product) = await CreateDatabaseWithProductAsync();

        await TestDatabase.AddAsync(
            databaseUrl,
            Movement(product, idempotencyKey: "retry-me"),
            Movement(product, idempotencyKey: "retry-me", actorId: TestDatabase.Ids.NewInternalId()));

        await using var db = TestDatabase.CreateContext(databaseUrl);
        Assert.Equal(2, await db.StockMovements.CountAsync(Token));
    }

    [Fact]
    public async Task MovementsWithoutAnIdempotencyKeyDoNotConflict()
    {
        var (databaseUrl, product) = await CreateDatabaseWithProductAsync();

        await TestDatabase.AddAsync(databaseUrl, Movement(product), Movement(product));

        await using var db = TestDatabase.CreateContext(databaseUrl);
        Assert.Equal(2, await db.StockMovements.CountAsync(Token));
    }

    [Fact]
    public async Task PublicIdsAreUnique()
    {
        var (databaseUrl, product) = await CreateDatabaseWithProductAsync();
        var first = Movement(product);
        await TestDatabase.AddAsync(databaseUrl, first);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(
            () => TestDatabase.AddAsync(databaseUrl, Movement(product, publicId: first.PublicId)));

        TestDatabase.AssertConstraintViolation(ex, PostgresErrorCodes.UniqueViolation, "ix_stock_movements_public_id");
    }

    [Fact]
    public async Task AVoidMustReferenceAnExistingMovement()
    {
        var (databaseUrl, product) = await CreateDatabaseWithProductAsync();
        var orphan = Movement(product, StockMovementType.Void, voidsMovementId: TestDatabase.Ids.NewInternalId());

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => TestDatabase.AddAsync(databaseUrl, orphan));

        TestDatabase.AssertConstraintViolation(ex, PostgresErrorCodes.ForeignKeyViolation, "fk_stock_movements_stock_movements_voids_movement_id");
    }

    [Fact]
    public async Task ProductHistoryIsIndexedByCreationTime()
    {
        var databaseUrl = await TestDatabase.CreateMigratedAsync(postgres, Token);

        await using var connection = new NpgsqlConnection(databaseUrl);
        await connection.OpenAsync(Token);
        await using var query = new NpgsqlCommand(
            "SELECT indexdef FROM pg_indexes WHERE indexname = 'ix_stock_movements_product_id_created_at'", connection);

        Assert.Equal(
            "CREATE INDEX ix_stock_movements_product_id_created_at ON public.stock_movements USING btree (product_id, created_at)",
            await query.ExecuteScalarAsync(Token));
    }

    private async Task<(string DatabaseUrl, Product Product)> CreateDatabaseWithProductAsync()
    {
        var databaseUrl = await TestDatabase.CreateMigratedAsync(postgres, Token);
        var product = TestProducts.New("SR-000001");
        await TestDatabase.AddAsync(databaseUrl, product);
        return (databaseUrl, product);
    }

    private static StockMovement Movement(
        Product product,
        StockMovementType type = StockMovementType.Receive,
        int delta = 1,
        int quantityAfter = 1,
        string? idempotencyKey = null,
        Guid? actorId = null,
        Guid? publicId = null,
        Guid? voidsMovementId = null) =>
        new()
        {
            Id = TestDatabase.Ids.NewInternalId(),
            PublicId = publicId ?? TestDatabase.Ids.NewPublicId(),
            ProductId = product.Id,
            LocationId = Location.MainStorageId,
            Type = type,
            Delta = delta,
            QuantityAfter = quantityAfter,
            Reason = StockMovementReason.Purchase,
            VoidsMovementId = voidsMovementId,
            IdempotencyKey = idempotencyKey,
            ActorId = actorId ?? Actor,
            CreatedAt = DateTimeOffset.UtcNow,
        };
}
