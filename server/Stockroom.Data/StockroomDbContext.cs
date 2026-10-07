using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Stockroom.Core.Locations;
using Stockroom.Core.Products;
using Stockroom.Core.Stock;
using Stockroom.Core.Users;
using Stockroom.Data.Products;
using Stockroom.Data.Settings;

namespace Stockroom.Data;

/// <summary>
/// The Stockroom database (PostgreSQL). Entity mappings and migrations live in this project.
/// Configure it with <see cref="StockroomDbContextOptions.UseStockroomDatabase"/> so the runtime and
/// the design-time tools agree on provider and naming.
/// </summary>
/// <remarks>
/// Users and roles are ASP.NET Core Identity's; their sets (<c>Users</c>, <c>Roles</c>, <c>UserRoles</c>, …)
/// come from <see cref="IdentityDbContext{TUser, TRole, TKey}"/>.
/// </remarks>
public sealed class StockroomDbContext(DbContextOptions<StockroomDbContext> options)
    : IdentityDbContext<User, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Product> Products => Set<Product>();

    public DbSet<ProductBarcode> ProductBarcodes => Set<ProductBarcode>();

    public DbSet<Location> Locations => Set<Location>();

    public DbSet<StockLevel> StockLevels => Set<StockLevel>();

    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    public DbSet<StoredSetting> Settings => Set<StoredSetting>();

    /// <summary>
    /// Pinned, so the schema does not depend on <c>IdentityOptions.Stores</c>, which the design-time
    /// factory cannot see. Version 3 adds passkeys, which Stockroom does not use.
    /// </summary>
    protected override Version SchemaVersion => IdentitySchemaVersions.Version2;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Identity's tables first, so the configurations below can rename and extend them.
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");

        modelBuilder.HasSequence<long>(SkuGenerator.SequenceName);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StockroomDbContext).Assembly);

        NameForeignKeysToIdentityTables(modelBuilder);
    }

    /// <summary>
    /// The snake_case convention names a foreign key when it is added, which for keys to users and roles is
    /// while their tables are still called <c>AspNetUsers</c> and <c>AspNetRoles</c>. Name those keys
    /// again, the same way the convention does, now that the tables are renamed.
    /// </summary>
    private static void NameForeignKeysToIdentityTables(ModelBuilder modelBuilder)
    {
        var foreignKeys = modelBuilder.Model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetForeignKeys())
            .Where(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(User)
                || foreignKey.PrincipalEntityType.ClrType == typeof(IdentityRole<Guid>));

        foreach (var foreignKey in foreignKeys)
        {
            var columns = string.Join('_', foreignKey.Properties.Select(property => property.GetColumnName()));
            foreignKey.SetConstraintName(
                $"fk_{foreignKey.DeclaringEntityType.GetTableName()}_{foreignKey.PrincipalEntityType.GetTableName()}_{columns}");
        }
    }
}
