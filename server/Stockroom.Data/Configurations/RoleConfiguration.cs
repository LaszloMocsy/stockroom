using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockroom.Core.Users;

namespace Stockroom.Data.Configurations;

internal sealed class RoleConfiguration : IEntityTypeConfiguration<IdentityRole<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityRole<Guid>> builder)
    {
        builder.ToTable("roles");

        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.HasIndex(r => r.NormalizedName).HasDatabaseName("ix_roles_normalized_name");

        // ADMIN and STAFF exist in every database from this migration on (spec 2).
        builder.HasData(Roles.All());
    }
}
