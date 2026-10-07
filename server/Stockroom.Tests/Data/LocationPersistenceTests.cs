using Microsoft.EntityFrameworkCore;
using Npgsql;
using Stockroom.Core.Locations;
using Stockroom.Tests.Infrastructure;

namespace Stockroom.Tests.Data;

public sealed class LocationPersistenceTests(PostgresFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MigrationSeedsTheDefaultLocationWithBothIds()
    {
        var databaseUrl = await TestDatabase.CreateMigratedAsync(postgres, Token);

        await using var db = TestDatabase.CreateContext(databaseUrl);
        var location = Assert.Single(await db.Locations.ToListAsync(Token));

        Assert.Equal(Location.MainStorageName, location.Name);
        Assert.Equal(Location.MainStorageId, location.Id);
        Assert.Equal(Location.MainStoragePublicId, location.PublicId);
        Assert.Equal(7, location.Id.Version);
        Assert.Equal(4, location.PublicId.Version);
    }

    [Fact]
    public async Task PublicIdsAreUnique()
    {
        var databaseUrl = await TestDatabase.CreateMigratedAsync(postgres, Token);
        var duplicate = new Location
        {
            Id = TestDatabase.Ids.NewInternalId(),
            PublicId = Location.MainStoragePublicId,
            Name = "Aisle 3",
        };

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => TestDatabase.AddAsync(databaseUrl, duplicate));

        TestDatabase.AssertConstraintViolation(ex, PostgresErrorCodes.UniqueViolation, "ix_locations_public_id");
    }
}
