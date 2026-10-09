using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockroom.Core.Products;

namespace Stockroom.Data.Configurations;

internal sealed class ProductBarcodeConfiguration : IEntityTypeConfiguration<ProductBarcode>
{
    public void Configure(EntityTypeBuilder<ProductBarcode> builder)
    {
        builder.ToTable("product_barcodes");

        builder.Property(b => b.Id).ValueGeneratedNever();

        // Unique across all products, so a scan resolves to at most one product.
        builder.HasIndex(b => b.Barcode).IsUnique();

        // For search by part of a barcode (ILIKE).
        builder.HasIndex(b => b.Barcode, "ix_product_barcodes_barcode_trgm").HasDatabaseName("ix_product_barcodes_barcode_trgm").HasMethod("gin").HasOperators("gin_trgm_ops");

        builder.HasOne<Product>()
            .WithMany(p => p.Barcodes)
            .HasForeignKey(b => b.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
