using Microsoft.EntityFrameworkCore;

namespace Stockroom.Data;

/// <summary>
/// The Stockroom database (PostgreSQL). Entities and their migrations live in this project.
/// Configure it with <see cref="StockroomDbContextOptions.UseStockroomDatabase"/> so the runtime and
/// the design-time tools agree on provider and naming.
/// </summary>
public sealed class StockroomDbContext(DbContextOptions<StockroomDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StockroomDbContext).Assembly);
}
