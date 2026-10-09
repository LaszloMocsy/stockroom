using System.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Stockroom.Api.Errors;
using Stockroom.Data;

namespace Stockroom.Api.Endpoints;

/// <summary><c>/api/v1/stats</c>: dashboard figures computed from products, stock levels, and the ledger (spec 7.1).</summary>
internal static class StatsEndpoints
{
    public const int RecentMovementCount = 10;

    public static IEndpointRouteBuilder MapStatsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var stats = endpoints.MapGroup("/stats").WithTags("Stats");

        stats.MapGet("/summary", GetSummaryAsync)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .WithName("GetStatsSummary")
            .WithSummary("Dashboard summary")
            .WithDescription($"""
                Counts active (not archived) products, the units they have on hand, and how many are low on stock (at or below their `min_stock`) or out of stock (none on hand, whether or not they have a `min_stock`). A product below zero, which only happens when negative stock is allowed, adds no units.

                `recent_movements` holds the {RecentMovementCount} newest movements of any product, newest first. All figures are read at the same moment.
                """);
        return endpoints;
    }

    private static async Task<Ok<StatsSummaryResponse>> GetSummaryAsync(StockroomDbContext db, CancellationToken cancellationToken)
    {
        // One snapshot for every query, so the figures agree with each other even while stock moves.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);

        // The quantity as ProductResponse computes it.
        var active = db.Products
            .Where(p => p.ArchivedAt == null)
            .Select(p => new
            {
                p.MinStock,
                Quantity = db.StockLevels.Where(l => l.ProductId == p.Id).Sum(l => (int?)l.Quantity) ?? 0,
            });

        var totalProducts = await active.CountAsync(cancellationToken);
        var totalUnits = await active.SumAsync(p => (long)Math.Max(p.Quantity, 0), cancellationToken);
        var lowStock = await active.CountAsync(p => p.MinStock != null && p.Quantity <= p.MinStock, cancellationToken);
        var outOfStock = await active.CountAsync(p => p.Quantity <= 0, cancellationToken);
        var recent = await StockMovementResponse.Project(db.StockMovements, db).Take(RecentMovementCount).ToListAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return TypedResults.Ok(new StatsSummaryResponse(totalProducts, totalUnits, lowStock, outOfStock, recent));
    }
}

/// <summary>Figures for the dashboard (spec 7.1).</summary>
/// <param name="TotalProducts">Active (not archived) products.</param>
/// <param name="TotalUnits">Units on hand across active products; a product below zero counts as none.</param>
/// <param name="LowStockCount">Active products at or below their <c>min_stock</c>.</param>
/// <param name="OutOfStockCount">Active products with no units on hand.</param>
/// <param name="RecentMovements">The newest movements of any product, newest first.</param>
public sealed record StatsSummaryResponse(
    int TotalProducts,
    long TotalUnits,
    int LowStockCount,
    int OutOfStockCount,
    IReadOnlyList<StockMovementResponse> RecentMovements);
