using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockroom.Core.Locations;
using Stockroom.Core.Products;
using Stockroom.Core.Stock;
using Stockroom.Core.Users;
using Stockroom.Data.Conversions;

namespace Stockroom.Data.Configurations;

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("stock_movements");

        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.PublicId).ValueGeneratedNever();
        builder.HasIndex(m => m.PublicId).IsUnique();

        builder.Property(m => m.Type).HasConversion<SnakeCaseEnumConverter<StockMovementType>>();
        builder.Property(m => m.Reason).HasConversion<SnakeCaseEnumConverter<StockMovementReason>>();

        // The ledger is append-only, so nothing it references may be deleted.
        builder.HasOne<Product>().WithMany().HasForeignKey(m => m.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Location>().WithMany().HasForeignKey(m => m.LocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StockMovement>().WithMany().HasForeignKey(m => m.VoidsMovementId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(m => m.ActorId).OnDelete(DeleteBehavior.Restrict);

        // A movement is voided at most once (spec 3.2, rule 2). StockService checks first, under the level's
        // row lock; this is the backstop. Movements that void nothing are NULL here, which never conflicts.
        builder.HasIndex(m => m.VoidsMovementId).IsUnique();

        // A product's history, newest or oldest first.
        builder.HasIndex(m => new { m.ProductId, m.CreatedAt });

        // A retried request from the same actor finds its original movement (spec 3.2, rule 8). Movements
        // without a key are unaffected, because PostgreSQL treats NULLs as distinct.
        builder.HasIndex(m => new { m.ActorId, m.IdempotencyKey }).IsUnique();
    }
}
