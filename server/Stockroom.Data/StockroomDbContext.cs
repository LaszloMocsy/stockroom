using Microsoft.EntityFrameworkCore;
using Stockroom.Core.Locations;
using Stockroom.Core.Products;
using Stockroom.Core.Stock;

namespace Stockroom.Data;

/// <summary>
/// The Stockroom database (PostgreSQL). Entity mappings and migrations live in this project.
/// Configure it with <see cref="StockroomDbContextOptions.UseStockroomDatabase"/> so the runtime and
/// the design-time tools agree on provider and naming.
/// </summary>
public sealed class StockroomDbContext(DbContextOptions<StockroomDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    public DbSet<ProductBarcode> ProductBarcodes => Set<ProductBarcode>();

    public DbSet<Location> Locations => Set<Location>();

    public DbSet<StockLevel> StockLevels => Set<StockLevel>();

    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StockroomDbContext).Assembly);
}
