using Microsoft.EntityFrameworkCore;
using Stockroom.Core.Identifiers;
using Stockroom.Core.Locations;
using Stockroom.Core.Settings;
using Stockroom.Core.Stock;

namespace Stockroom.Data.Stock;

/// <summary>
/// The only code that changes stock (spec 3.2). Each operation appends a movement to the ledger and
/// updates the cached stock level in one transaction, so the two cannot disagree. Shares the caller's
/// <see cref="StockroomDbContext"/>: inside a transaction in progress it joins that one, otherwise it
/// starts its own.
/// </summary>
/// <remarks>
/// <para>
/// Products and actors are internal IDs; resolving public IDs is the API's job. Until locations arrive
/// (P1), every movement is at <see cref="Location.MainStorageId"/>.
/// </para>
/// <para>
/// Requests may carry an idempotency key (spec 3.2, rule 8). A request whose actor already made a movement
/// with that key returns that movement and writes nothing, without validating anything else, so a client
/// can safely retry after a lost response. Only movements remember keys: a request that failed or changed
/// nothing is evaluated afresh when retried.
/// </para>
/// </remarks>
public sealed class StockService(StockroomDbContext db, ISettingsStore settings, IIdGenerator ids, TimeProvider time)
{
    /// <summary>
    /// Records the quantity a new product starts with and returns the <c>initial</c> movement. Call it in the
    /// transaction that creates the product, so the product never exists without its starting stock.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The quantity is not positive.</exception>
    /// <exception cref="InvalidOperationException">The product already has stock history; nothing is written.</exception>
    public Task<StockMovement> RecordInitialAsync(InitialStock request, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.Quantity);

        return InTransactionAsync(async () =>
        {
            // Only movements create level rows, so an existing row means the product already has history.
            // Inserting rather than checking first keeps two concurrent calls from both succeeding.
            if (!await CreateLevelAsync(request.ProductId, Location.MainStorageId, request.Quantity, cancellationToken))
            {
                throw new InvalidOperationException("Only a product without stock history can be given an initial quantity.");
            }

            return await AppendAsync(
                request.ProductId, StockMovementType.Initial, request.Quantity, request.Quantity,
                request.Reason, request.Note, request.Reference, request.ActorId, idempotencyKey: null, cancellationToken);
        }, cancellationToken);
    }

    /// <summary>Adds <see cref="ReceiveStock.Quantity"/> units and returns the <c>receive</c> movement.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The quantity is not positive.</exception>
    public Task<StockMovement> ReceiveAsync(ReceiveStock request, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.Quantity);

        return InTransactionAsync(async () =>
        {
            if (await FindRetriedAsync(request.ActorId, request.IdempotencyKey, cancellationToken) is { } retried)
            {
                return retried;
            }

            var quantityAfter = await AddToLevelAsync(request.ProductId, Location.MainStorageId, request.Quantity, cancellationToken);
            return await AppendAsync(
                request.ProductId, StockMovementType.Receive, request.Quantity, quantityAfter,
                request.Reason, request.Note, request.Reference, request.ActorId, request.IdempotencyKey, cancellationToken);
        }, cancellationToken);
    }

    /// <summary>Removes <see cref="IssueStock.Quantity"/> units and returns the <c>issue</c> movement.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The quantity is not positive.</exception>
    /// <exception cref="InsufficientStockException">
    /// Fewer units are on hand than requested and <see cref="StockroomSettings.AllowNegativeStock"/> is off;
    /// nothing is written.
    /// </exception>
    public Task<StockMovement> IssueAsync(IssueStock request, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.Quantity);

        return InTransactionAsync(async () =>
        {
            if (await FindRetriedAsync(request.ActorId, request.IdempotencyKey, cancellationToken) is { } retried)
            {
                return retried;
            }

            // The lock holds off concurrent movements until this transaction ends, so the quantity checked
            // is still the quantity when the level is updated, and parallel issues cannot oversell. The
            // setting is read only when it matters, so a normal issue costs no extra query.
            var available = await LockLevelAsync(request.ProductId, Location.MainStorageId, cancellationToken);
            if (available < request.Quantity
                && !await settings.GetAsync(StockroomSettings.AllowNegativeStock, cancellationToken))
            {
                throw new InsufficientStockException(request.Quantity, available);
            }

            var quantityAfter = await AddToLevelAsync(request.ProductId, Location.MainStorageId, -request.Quantity, cancellationToken);
            return await AppendAsync(
                request.ProductId, StockMovementType.Issue, -request.Quantity, quantityAfter,
                request.Reason, request.Note, request.Reference, request.ActorId, request.IdempotencyKey, cancellationToken);
        }, cancellationToken);
    }

    /// <summary>
    /// Sets the quantity to <see cref="AdjustStock.TargetQuantity"/> and returns the <c>adjust</c> movement,
    /// whose delta is the difference. Returns <see langword="null"/> and writes nothing when the quantity is
    /// already the target.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The target quantity is negative.</exception>
    /// <exception cref="StockConflictException">
    /// The quantity is not <see cref="AdjustStock.ExpectedCurrent"/>; nothing is written.
    /// </exception>
    public Task<StockMovement?> AdjustAsync(AdjustStock request, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(request.TargetQuantity);

        return InTransactionAsync<StockMovement?>(async () =>
        {
            if (await FindRetriedAsync(request.ActorId, request.IdempotencyKey, cancellationToken) is { } retried)
            {
                return retried;
            }

            // Locked, so the quantity compared and the delta computed still hold when the level is updated.
            var current = await LockLevelAsync(request.ProductId, Location.MainStorageId, cancellationToken);
            if (request.ExpectedCurrent is { } expected && expected != current)
            {
                throw new StockConflictException(expected, current);
            }

            var delta = request.TargetQuantity - current;
            if (delta == 0)
            {
                return null;
            }

            var quantityAfter = await AddToLevelAsync(request.ProductId, Location.MainStorageId, delta, cancellationToken);
            return await AppendAsync(
                request.ProductId, StockMovementType.Adjust, delta, quantityAfter,
                request.Reason, request.Note, request.Reference, request.ActorId, request.IdempotencyKey, cancellationToken);
        }, cancellationToken);
    }

    /// <summary>
    /// Reverses a movement with a linked <c>void</c> movement whose delta is the opposite of the original's,
    /// and returns it. The original stays in the ledger unchanged (spec 3.2, rule 2).
    /// </summary>
    /// <exception cref="KeyNotFoundException">No movement has the ID.</exception>
    /// <exception cref="MovementNotVoidableException">
    /// The movement was already voided, or is itself a void; nothing is written.
    /// </exception>
    /// <exception cref="InsufficientStockException">
    /// Reversing would take the quantity below zero and <see cref="StockroomSettings.AllowNegativeStock"/> is
    /// off; nothing is written.
    /// </exception>
    public Task<StockMovement> VoidAsync(VoidStock request, CancellationToken cancellationToken) =>
        InTransactionAsync(async () =>
        {
            if (await FindRetriedAsync(request.ActorId, request.IdempotencyKey, cancellationToken) is { } retried)
            {
                return retried;
            }

            var original = await db.StockMovements
                .AsNoTracking()
                .Where(m => m.Id == request.MovementId)
                .Select(m => new { m.ProductId, m.LocationId, m.Type, m.Delta })
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException($"No stock movement has the ID {request.MovementId}.");
            if (original.Type == StockMovementType.Void)
            {
                throw new MovementNotVoidableException(MovementNotVoidableReason.IsVoid);
            }

            // Every void of this movement takes the same lock, so only one of them can find it not yet voided.
            var current = await LockLevelAsync(original.ProductId, original.LocationId, cancellationToken);
            if (await db.StockMovements.AnyAsync(m => m.VoidsMovementId == request.MovementId, cancellationToken))
            {
                throw new MovementNotVoidableException(MovementNotVoidableReason.AlreadyVoided);
            }

            var delta = -original.Delta;
            if (current + delta < 0 && delta < 0
                && !await settings.GetAsync(StockroomSettings.AllowNegativeStock, cancellationToken))
            {
                throw new InsufficientStockException(-delta, current);
            }

            var quantityAfter = await AddToLevelAsync(original.ProductId, original.LocationId, delta, cancellationToken);
            return await AppendAsync(
                original.ProductId, original.LocationId, StockMovementType.Void, delta, quantityAfter,
                request.Reason, request.Note, reference: null, request.MovementId, request.ActorId, request.IdempotencyKey,
                cancellationToken);
        }, cancellationToken);

    /// <summary>
    /// The movement the actor already made with <paramref name="idempotencyKey"/>, or <see langword="null"/>
    /// if there is none or no key. Takes a lock on the key until the transaction ends, so a concurrent
    /// duplicate waits for this request and then finds its movement instead of writing a second one.
    /// </summary>
    /// <exception cref="ArgumentException">The key is empty or whitespace.</exception>
    private async Task<StockMovement?> FindRetriedAsync(Guid actorId, string? idempotencyKey, CancellationToken cancellationToken)
    {
        if (idempotencyKey is null)
        {
            return null;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        await db.Database.AcquireTransactionLockAsync($"idempotency:{actorId}:{idempotencyKey}", cancellationToken);
        return await db.StockMovements
            .AsNoTracking()
            .SingleOrDefaultAsync(m => m.ActorId == actorId && m.IdempotencyKey == idempotencyKey, cancellationToken);
    }

    /// <summary>Runs <paramref name="operation"/> in the caller's transaction, or in a new one it commits on success.</summary>
    private async Task<T> InTransactionAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            return await operation();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var result = await operation();
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    /// <summary>
    /// Locks the level row until the transaction ends and returns its quantity: 0 if the product has never
    /// had stock at the location, in which case there is no row to lock.
    /// </summary>
    private async Task<int> LockLevelAsync(Guid productId, Guid locationId, CancellationToken cancellationToken) =>
        (await db.Database
            .SqlQuery<int>(
                $"""
                SELECT quantity AS "Value" FROM stock_levels
                WHERE product_id = {productId} AND location_id = {locationId}
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken))
            .SingleOrDefault();

    /// <summary>
    /// Creates the level row with <paramref name="quantity"/>. Returns <see langword="false"/>, changing
    /// nothing, if the row already exists.
    /// </summary>
    private async Task<bool> CreateLevelAsync(Guid productId, Guid locationId, int quantity, CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO stock_levels (product_id, location_id, quantity) VALUES ({productId}, {locationId}, {quantity})
            ON CONFLICT (product_id, location_id) DO NOTHING
            """,
            cancellationToken) == 1;

    /// <summary>
    /// Adds <paramref name="delta"/> to the level, creating the row on the first movement, and returns the
    /// new quantity. One statement, so concurrent movements queue on the row lock and each sees the
    /// others' results, and two first movements cannot both try to create the row.
    /// </summary>
    private async Task<int> AddToLevelAsync(Guid productId, Guid locationId, int delta, CancellationToken cancellationToken) =>
        // EF would wrap a composed query in a SELECT, which cannot contain an INSERT, so read the list instead.
        (await db.Database
            .SqlQuery<int>(
                $"""
                INSERT INTO stock_levels (product_id, location_id, quantity) VALUES ({productId}, {locationId}, {delta})
                ON CONFLICT (product_id, location_id) DO UPDATE SET quantity = stock_levels.quantity + excluded.quantity
                RETURNING quantity AS "Value"
                """)
            .ToListAsync(cancellationToken))
            .Single();

    private Task<StockMovement> AppendAsync(
        Guid productId,
        StockMovementType type,
        int delta,
        int quantityAfter,
        StockMovementReason reason,
        string? note,
        string? reference,
        Guid actorId,
        string? idempotencyKey,
        CancellationToken cancellationToken) =>
        AppendAsync(
            productId, Location.MainStorageId, type, delta, quantityAfter,
            reason, note, reference, voidsMovementId: null, actorId, idempotencyKey, cancellationToken);

    private async Task<StockMovement> AppendAsync(
        Guid productId,
        Guid locationId,
        StockMovementType type,
        int delta,
        int quantityAfter,
        StockMovementReason reason,
        string? note,
        string? reference,
        Guid? voidsMovementId,
        Guid actorId,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var movement = new StockMovement
        {
            Id = ids.NewInternalId(),
            PublicId = ids.NewPublicId(),
            ProductId = productId,
            LocationId = locationId,
            Type = type,
            Delta = delta,
            QuantityAfter = quantityAfter,
            Reason = reason,
            Note = note,
            Reference = reference,
            VoidsMovementId = voidsMovementId,
            IdempotencyKey = idempotencyKey,
            ActorId = actorId,
            // PostgreSQL stores microseconds, but the clock can be finer (100 ns ticks on Linux). Round down so the
            // movement returned here is the one stored, and a retry returns exactly the original (spec 3.2, rule 8).
            CreatedAt = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond)),
        };
        db.StockMovements.Add(movement);
        await db.SaveChangesAsync(cancellationToken);
        return movement;
    }
}
