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
/// Products and actors are internal IDs; resolving public IDs is the API's job. Until locations arrive
/// (P1), every movement is at <see cref="Location.MainStorageId"/>.
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
                request.Reason, request.Note, request.Reference, request.ActorId, cancellationToken);
        }, cancellationToken);
    }

    /// <summary>Adds <see cref="ReceiveStock.Quantity"/> units and returns the <c>receive</c> movement.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The quantity is not positive.</exception>
    public Task<StockMovement> ReceiveAsync(ReceiveStock request, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.Quantity);

        return InTransactionAsync(async () =>
        {
            var quantityAfter = await AddToLevelAsync(request.ProductId, Location.MainStorageId, request.Quantity, cancellationToken);
            return await AppendAsync(
                request.ProductId, StockMovementType.Receive, request.Quantity, quantityAfter,
                request.Reason, request.Note, request.Reference, request.ActorId, cancellationToken);
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
                request.Reason, request.Note, request.Reference, request.ActorId, cancellationToken);
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
                request.Reason, request.Note, request.Reference, request.ActorId, cancellationToken);
        }, cancellationToken);
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

    private async Task<StockMovement> AppendAsync(
        Guid productId,
        StockMovementType type,
        int delta,
        int quantityAfter,
        StockMovementReason reason,
        string? note,
        string? reference,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        var movement = new StockMovement
        {
            Id = ids.NewInternalId(),
            PublicId = ids.NewPublicId(),
            ProductId = productId,
            LocationId = Location.MainStorageId,
            Type = type,
            Delta = delta,
            QuantityAfter = quantityAfter,
            Reason = reason,
            Note = note,
            Reference = reference,
            ActorId = actorId,
            CreatedAt = time.GetUtcNow(),
        };
        db.StockMovements.Add(movement);
        await db.SaveChangesAsync(cancellationToken);
        return movement;
    }
}
