using Microsoft.EntityFrameworkCore;
using Stockroom.Core.Products;

namespace Stockroom.Data.Products;

/// <summary>
/// Numbers SKUs from a PostgreSQL sequence. <c>nextval</c> is atomic, so concurrent callers always get
/// different numbers. It is not transactional: a number taken by a rolled-back transaction is skipped,
/// leaving a gap.
/// </summary>
public sealed class SkuGenerator(StockroomDbContext db) : ISkuGenerator
{
    /// <summary>The sequence behind generated SKUs; created by the migrations.</summary>
    public const string SequenceName = "sku_numbers";

    public async Task<string> NextAsync(CancellationToken cancellationToken)
    {
        // EF reads a scalar query's result from a column named "Value".
        var number = await db.Database
            .SqlQueryRaw<long>($"SELECT nextval('{SequenceName}') AS \"Value\"")
            .SingleAsync(cancellationToken);

        return Sku.FromNumber(number);
    }
}
