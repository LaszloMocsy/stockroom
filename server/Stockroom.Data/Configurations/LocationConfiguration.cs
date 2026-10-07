using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockroom.Core.Locations;

namespace Stockroom.Data.Configurations;

internal sealed class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.ToTable("locations");

        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.PublicId).ValueGeneratedNever();
        builder.HasIndex(l => l.PublicId).IsUnique();

        // Every database has the default location from its first migration on (spec 3.1).
        builder.HasData(Location.MainStorage());
    }
}
