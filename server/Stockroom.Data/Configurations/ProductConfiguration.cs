using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockroom.Core.Products;

namespace Stockroom.Data.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");

        // Both IDs come from IIdGenerator, never from the database.
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.PublicId).ValueGeneratedNever();
        builder.HasIndex(p => p.PublicId).IsUnique();

        builder.HasIndex(p => p.Sku).IsUnique();

        // The foreign key to users is added together with the users table (task D1).
        builder.Property(p => p.CreatedBy);
    }
}
