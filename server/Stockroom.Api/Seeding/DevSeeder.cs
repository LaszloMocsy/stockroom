using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Stockroom.Api.Endpoints;
using Stockroom.Core.Identifiers;
using Stockroom.Core.Products;
using Stockroom.Core.Settings;
using Stockroom.Core.Stock;
using Stockroom.Core.Users;
using Stockroom.Data;
using Stockroom.Data.Stock;

namespace Stockroom.Api.Seeding;

/// <summary>A user the dev seed creates. The password is well known, which is why seeding is for development only.</summary>
internal sealed record DevSeedUser(string Username, string DisplayName, string Password, string Role);

/// <summary>What one seed run created; all zeros when the database was already seeded.</summary>
internal sealed record DevSeedResult(int UsersCreated, int ProductsCreated, int MovementsCreated);

/// <summary>
/// Fills a development database with an ADMIN, a STAFF user, and the <see cref="DevSeedCatalogue"/> products
/// with a few months of stock history (spec 14). Idempotent: users are matched by username and products by
/// SKU, and only missing ones are created, so a second run writes nothing.
/// </summary>
/// <remarks>
/// Stock changes go through <see cref="StockService"/> like any other (spec 3.2), with a clock set back in
/// time so the history is spread over past days. Each product's history comes from a random generator
/// seeded with its place in the catalogue, so it is the same on every database.
/// </remarks>
internal sealed class DevSeeder(StockroomDbContext db, UserManager<User> users, ISettingsStore settings, TimeProvider time)
{
    public static readonly DevSeedUser Admin = new("admin", "Ada Admin", "stockroom-admin", Roles.Admin);

    public static readonly DevSeedUser Staff = new("staff", "Sam Staff", "stockroom-staff", Roles.Staff);

    private static readonly string?[] IssueNotes = [null, "Reception", "Workshop", "Meeting room 2", "New starter kit", "Marketing team"];

    public async Task<DevSeedResult> SeedAsync(CancellationToken cancellationToken)
    {
        // All or nothing. The user creation lock is held to the end, so a concurrent seed waits and then finds
        // everything this one created.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.AcquireTransactionLockAsync(AdvisoryLocks.UserCreation, cancellationToken);

        var (adminId, adminCreated) = await EnsureUserAsync(Admin);
        var (staffId, staffCreated) = await EnsureUserAsync(Staff);

        var catalogue = DevSeedCatalogue.Products;
        var skus = catalogue.Select(p => p.Sku).ToList();
        var existing = await db.Products.Where(p => skus.Contains(p.Sku)).Select(p => p.Sku).ToListAsync(cancellationToken);

        // A barcode someone attached by hand to another product is left off rather than failing the run.
        var barcodes = catalogue.SelectMany((p, i) => Enumerable.Range(0, p.Barcodes).Select(b => DevSeedCatalogue.Barcode(i, b))).ToList();
        var takenBarcodes = await db.ProductBarcodes.Where(b => barcodes.Contains(b.Barcode)).Select(b => b.Barcode).ToListAsync(cancellationToken);

        var clock = new SeedClock();
        var ids = new IdGenerator(clock);
        var history = new History(db, new StockService(db, settings, ids, clock), ids, clock, time.GetUtcNow(), adminId, staffId);
        var productsCreated = 0;
        var movementsCreated = 0;
        for (var i = 0; i < catalogue.Count; i++)
        {
            if (!existing.Contains(catalogue[i].Sku))
            {
                movementsCreated += await history.CreateProductAsync(i, catalogue[i], takenBarcodes, cancellationToken);
                productsCreated++;
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return new DevSeedResult((adminCreated ? 1 : 0) + (staffCreated ? 1 : 0), productsCreated, movementsCreated);
    }

    /// <summary>The internal ID of the user named <see cref="DevSeedUser.Username"/>, created if there is none.</summary>
    private async Task<(Guid Id, bool Created)> EnsureUserAsync(DevSeedUser seed)
    {
        if (await users.FindByNameAsync(seed.Username) is { } user)
        {
            return (user.Id, false);
        }

        user = new User { UserName = seed.Username, DisplayName = seed.DisplayName };
        UserAccounts.EnsureSucceeded(await users.CreateAsync(user, seed.Password), $"create the seed user {seed.Username}");
        UserAccounts.EnsureSucceeded(await users.AddToRoleAsync(user, seed.Role), $"add the seed user {seed.Username} to {seed.Role}");
        return (user.Id, true);
    }

    /// <summary>Creates products and makes up their stock history.</summary>
    private sealed class History(StockroomDbContext db, StockService stock, IIdGenerator ids, SeedClock clock, DateTimeOffset now, Guid adminId, Guid staffId)
    {
        private readonly DateTimeOffset _today = new(now.UtcDateTime.Date, TimeSpan.Zero);

        /// <summary>Creates the product at <paramref name="index"/> in the catalogue and returns how many movements it got.</summary>
        public async Task<int> CreateProductAsync(int index, DevSeedProduct item, IReadOnlyCollection<string> takenBarcodes, CancellationToken cancellationToken)
        {
            var random = new Random(index);
            var daysAgo = random.Next(30, 91);
            clock.Now = Moment(daysAgo, random);

            var creator = index % 3 == 0 ? staffId : adminId;
            var product = new Product
            {
                Id = ids.NewInternalId(),
                PublicId = ids.NewPublicId(),
                Sku = item.Sku,
                Name = item.Name,
                Description = item.Description,
                MinStock = item.MinStock,
                CreatedAt = clock.Now,
                UpdatedAt = clock.Now,
                CreatedBy = creator,
            };
            foreach (var barcode in Enumerable.Range(0, item.Barcodes).Select(b => DevSeedCatalogue.Barcode(index, b)).Except(takenBarcodes))
            {
                product.Barcodes.Add(new ProductBarcode { Id = ids.NewInternalId(), ProductId = product.Id, Barcode = barcode });
            }

            db.Products.Add(product);
            await db.SaveChangesAsync(cancellationToken);

            var movements = new List<StockMovement>();
            var quantity = 0;
            void Record(StockMovement? movement)
            {
                if (movement is not null)
                {
                    movements.Add(movement);
                    quantity = movement.QuantityAfter;
                }
            }

            // Most products start with stock; the rest are created empty and stocked by a later delivery.
            if (random.Next(5) > 0)
            {
                Record(await stock.RecordInitialAsync(new InitialStock(product.Id, random.Next(5, 60), creator), cancellationToken));
            }

            while ((daysAgo -= random.Next(1, 8)) > 0)
            {
                clock.Now = Moment(daysAgo, random);
                var actor = random.Next(3) == 0 ? adminId : staffId;
                var last = movements.LastOrDefault();
                Record(random.Next(100) switch
                {
                    < 30 => await stock.ReceiveAsync(
                        new ReceiveStock(product.Id, random.Next(5, 41), actor) { Reference = $"PO-2026-{random.Next(100, 1000)}" },
                        cancellationToken),
                    < 85 when quantity > 0 => await stock.IssueAsync(
                        random.Next(10) == 0
                            ? new IssueStock(product.Id, 1, actor) { Reason = StockMovementReason.Damaged, Note = "Found damaged on the shelf" }
                            : new IssueStock(product.Id, random.Next(1, Math.Min(quantity, 12) + 1), actor)
                            {
                                Reason = StockMovementReason.Other,
                                Note = IssueNotes[random.Next(IssueNotes.Length)],
                            },
                        cancellationToken),
                    < 95 => await stock.AdjustAsync(
                        new AdjustStock(product.Id, Math.Max(0, quantity + random.Next(-3, 3)), actor) { ExpectedCurrent = quantity, Note = "Monthly count" },
                        cancellationToken),
                    _ when last is { Type: not StockMovementType.Void } && quantity - last.Delta >= 0 => await stock.VoidAsync(
                        new VoidStock(last.Id, adminId) { Note = "Entered twice" },
                        cancellationToken),
                    _ => null,
                });
            }

            // A few products end out of stock or low, so the dashboard and the low-stock list have something to show.
            clock.Now = Moment(0, random);
            if (index % 10 == 3 && quantity > 0)
            {
                Record(await stock.IssueAsync(new IssueStock(product.Id, quantity, staffId) { Reason = StockMovementReason.Other, Note = "Used up" }, cancellationToken));
            }
            else if (index % 10 == 7 && item.MinStock is { } minStock && quantity > minStock)
            {
                Record(await stock.IssueAsync(new IssueStock(product.Id, quantity - minStock, staffId) { Reason = StockMovementReason.Other }, cancellationToken));
            }

            return movements.Count;
        }

        /// <summary>A time during working hours <paramref name="daysAgo"/> days ago, but never later than now.</summary>
        private DateTimeOffset Moment(int daysAgo, Random random)
        {
            var moment = _today.AddDays(-daysAgo).AddHours(8).AddMinutes(random.Next(9 * 60));
            return moment < now ? moment : now;
        }
    }

    /// <summary>A clock that reads whatever time it was last set to, for back-dating the seeded history.</summary>
    private sealed class SeedClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
