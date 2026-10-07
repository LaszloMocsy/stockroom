using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Stockroom.Core.Users;
using Stockroom.Tests.Api;
using Stockroom.Tests.Infrastructure;

namespace Stockroom.Tests.Data;

public sealed class UserPersistenceTests(PostgresFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MigrationSeedsTheAdminAndStaffRoles()
    {
        var databaseUrl = await TestDatabase.CreateMigratedAsync(postgres, Token);

        await using var db = TestDatabase.CreateContext(databaseUrl);
        var roles = await db.Roles.OrderBy(r => r.Name).ToListAsync(Token);

        Assert.Collection(
            roles,
            admin =>
            {
                Assert.Equal((Roles.AdminId, Roles.Admin, Roles.Admin), (admin.Id, admin.Name, admin.NormalizedName));
                Assert.Equal(7, admin.Id.Version);
            },
            staff =>
            {
                Assert.Equal((Roles.StaffId, Roles.Staff, Roles.Staff), (staff.Id, staff.Name, staff.NormalizedName));
                Assert.Equal(7, staff.Id.Version);
            });
    }

    [Fact]
    public async Task UsersCreatedThroughIdentityGetBothIds()
    {
        await using var factory = await StockroomApiFactory.CreateAsync(postgres);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = new User { UserName = "anna", DisplayName = "Anna Kovács" };

            AssertSucceeded(await users.CreateAsync(user, "Correct-Horse-1"));
            AssertSucceeded(await users.AddToRoleAsync(user, Roles.Staff));
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var loaded = await users.FindByNameAsync("anna");

            Assert.NotNull(loaded);
            Assert.Equal(7, loaded.Id.Version);
            Assert.Equal(4, loaded.PublicId.Version);
            Assert.Equal("Anna Kovács", loaded.DisplayName);
            Assert.True(await users.CheckPasswordAsync(loaded, "Correct-Horse-1"));
            Assert.Equal([Roles.Staff], await users.GetRolesAsync(loaded));
        }
    }

    [Fact]
    public async Task UserNamesAreUnique()
    {
        var databaseUrl = await TestDatabase.CreateMigratedAsync(postgres, Token);
        await TestDatabase.AddAsync(databaseUrl, TestUsers.New("anna"));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => TestDatabase.AddAsync(databaseUrl, TestUsers.New("anna")));

        TestDatabase.AssertConstraintViolation(ex, PostgresErrorCodes.UniqueViolation, "ix_users_normalized_user_name");
    }

    [Fact]
    public async Task PublicIdsAreUnique()
    {
        var databaseUrl = await TestDatabase.CreateMigratedAsync(postgres, Token);
        var first = TestUsers.New("anna");
        await TestDatabase.AddAsync(databaseUrl, first);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(
            () => TestDatabase.AddAsync(databaseUrl, TestUsers.New("bela", publicId: first.PublicId)));

        TestDatabase.AssertConstraintViolation(ex, PostgresErrorCodes.UniqueViolation, "ix_users_public_id");
    }

    private static void AssertSucceeded(IdentityResult result) =>
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
}
