using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockroom.Core.Auth;
using Stockroom.Core.Users;

namespace Stockroom.Data.Configurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.PublicId).ValueGeneratedNever();
        builder.HasIndex(t => t.PublicId).IsUnique();

        // Refreshing looks a token up by its hash.
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.Property(t => t.DeviceName).HasMaxLength(100);

        // Tokens belong to their user and have no history worth keeping without it.
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
