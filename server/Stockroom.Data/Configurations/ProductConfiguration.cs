using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockroom.Core.Products;
using Stockroom.Core.Users;

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

        // Users are never deleted (P1 adds disabling instead), and history must keep its author.
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.CreatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}
