using Microsoft.EntityFrameworkCore;

namespace Stockroom.Data.Stock;

/// <summary>
/// Checks the cached stock levels against the ledger (spec 3.2, rule 6). It only reads: repairing drift
/// is a decision for a person, and only <see cref="StockService"/> writes stock.
/// </summary>
public sealed class StockReconciler(StockroomDbContext db)
{
    /// <summary>
    /// Every product and location whose cached level differs from the sum of its movements' deltas, ordered
    /// by product and location. A missing level row or a level without movements counts as 0.
    /// </summary>
    /// <remarks>
    /// One statement, so it reads a single snapshot: a movement and its level update commit together, so
    /// one in flight never shows up as drift. It reads the whole ledger, which is fine at MVP scale.
    /// </remarks>
    public async Task<IReadOnlyList<StockDrift>> FindDriftAsync(CancellationToken cancellationToken) =>
        await db.Database
            .SqlQuery<StockDrift>(
                $"""
                WITH ledger AS (
                    SELECT product_id, location_id, SUM(delta) AS quantity
                    FROM stock_movements
                    GROUP BY product_id, location_id
                ),
                compared AS (
                    SELECT
                        COALESCE(ledger.product_id, levels.product_id) AS product_id,
                        COALESCE(ledger.location_id, levels.location_id) AS location_id,
                        COALESCE(levels.quantity, 0) AS cached_quantity,
                        COALESCE(ledger.quantity, 0) AS ledger_quantity
                    FROM ledger
                    FULL OUTER JOIN stock_levels AS levels
                        ON levels.product_id = ledger.product_id AND levels.location_id = ledger.location_id
                )
                SELECT
                    compared.product_id, products.public_id AS product_public_id, compared.location_id,
                    compared.cached_quantity, compared.ledger_quantity
                FROM compared
                JOIN products ON products.id = compared.product_id
                WHERE compared.cached_quantity <> compared.ledger_quantity
                ORDER BY compared.product_id, compared.location_id
                """)
            .ToListAsync(cancellationToken);
}

/// <summary>A product and location whose cached level disagrees with the ledger.</summary>
/// <param name="ProductId">Internal ID of the product.</param>
/// <param name="ProductPublicId">Public ID of the product, for logs and reports.</param>
/// <param name="LocationId">Internal ID of the location.</param>
/// <param name="CachedQuantity">The quantity in <c>stock_levels</c>; 0 if the row is missing.</param>
/// <param name="LedgerQuantity">The sum of the movements' deltas; 0 if there are none.</param>
public sealed record StockDrift(Guid ProductId, Guid ProductPublicId, Guid LocationId, int CachedQuantity, long LedgerQuantity)
{
    /// <summary>How far the cached level is from the ledger: positive when it shows too much stock.</summary>
    public long Difference => CachedQuantity - LedgerQuantity;
}
