using Microsoft.EntityFrameworkCore;
using Stockroom.Core.Locations;
using Stockroom.Core.Products;
using Stockroom.Core.Stock;
using Stockroom.Data.Products;
using Stockroom.Data.Settings;

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

    public DbSet<StoredSetting> Settings => Set<StoredSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>(SkuGenerator.SequenceName);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StockroomDbContext).Assembly);
    }
}
