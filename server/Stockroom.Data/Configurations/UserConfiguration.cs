using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockroom.Core.Users;

namespace Stockroom.Data.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        // Both IDs come from IIdGenerator (see StockroomUserStore), never from the database.
        builder.Property(u => u.Id).ValueGeneratedNever();
        builder.Property(u => u.PublicId).ValueGeneratedNever();
        builder.HasIndex(u => u.PublicId).IsUnique();

        builder.HasIndex(u => u.NormalizedUserName).HasDatabaseName("ix_users_normalized_user_name");
        builder.HasIndex(u => u.NormalizedEmail).HasDatabaseName("ix_users_normalized_email");
    }
}
