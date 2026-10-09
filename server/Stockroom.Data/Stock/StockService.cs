using Microsoft.EntityFrameworkCore;
using Stockroom.Core.Identifiers;
using Stockroom.Core.Locations;
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
public sealed class StockService(StockroomDbContext db, IIdGenerator ids, TimeProvider time)
{
    /// <summary>Adds <see cref="ReceiveStock.Quantity"/> units and returns the <c>receive</c> movement.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The quantity is not positive.</exception>
    public async Task<StockMovement> ReceiveAsync(ReceiveStock request, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.Quantity);

        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var quantityAfter = await AddToLevelAsync(request.ProductId, Location.MainStorageId, request.Quantity, cancellationToken);
        var movement = new StockMovement
        {
            Id = ids.NewInternalId(),
            PublicId = ids.NewPublicId(),
            ProductId = request.ProductId,
            LocationId = Location.MainStorageId,
            Type = StockMovementType.Receive,
            Delta = request.Quantity,
            QuantityAfter = quantityAfter,
            Reason = request.Reason,
            Note = request.Note,
            Reference = request.Reference,
            ActorId = request.ActorId,
            CreatedAt = time.GetUtcNow(),
        };
        db.StockMovements.Add(movement);
        await db.SaveChangesAsync(cancellationToken);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return movement;
    }

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
}
