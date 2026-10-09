using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stockroom.Api.Seeding;
using Stockroom.Core.Users;
using Stockroom.Data;
using Stockroom.Data.Stock;
using Stockroom.Tests.Data;
using Stockroom.Tests.Infrastructure;

namespace Stockroom.Tests.Api;

/// <summary>The development seed (<see cref="DevSeeder"/>) and the <c>seed</c> command that runs it.</summary>
public sealed class DevSeedTests(PostgresFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SeedingCreatesTheUsersAndProductsWithTheirHistory()
    {
        await using var app = await StockroomApiFactory.CreateAsync(postgres);

        var result = await SeedAsync(app);

        Assert.Equal(2, result.UsersCreated);
        Assert.Equal(DevSeedCatalogue.Products.Count, result.ProductsCreated);
        Assert.InRange(DevSeedCatalogue.Products.Count, 45, 55);
        var contents = await ContentsAsync(app);
        Assert.Equal(DevSeedCatalogue.Products.Count, contents.Products);
        Assert.Equal(result.MovementsCreated, contents.Movements);
        Assert.True(contents.Movements > 5 * contents.Products, $"Only {contents.Movements} movements.");

        await using (var scope = app.Services.CreateAsyncScope())
        {
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<StockReconciler>().FindDriftAsync(Token));
        }

        // Both users sign in with the printed passwords and have their roles.
        using var client = app.CreateClient();
        foreach (var user in new[] { DevSeeder.Admin, DevSeeder.Staff })
        {
            using var me = await GetAsAsync(client, user, "/api/v1/me");
            Assert.Equal(user.Role, me.RootElement.GetProperty("role").GetString());
        }

        // The data has something for every dashboard figure.
        using var summary = await GetAsAsync(client, DevSeeder.Staff, "/api/v1/stats/summary");
        Assert.True(summary.RootElement.GetProperty("low_stock_count").GetInt32() > 0);
        Assert.True(summary.RootElement.GetProperty("out_of_stock_count").GetInt32() > 0);
        Assert.Equal(10, summary.RootElement.GetProperty("recent_movements").GetArrayLength());
    }

    [Fact]
    public async Task SeedingTwiceChangesNothing()
    {
        await using var app = await StockroomApiFactory.CreateAsync(postgres);
        await SeedAsync(app);
        var before = await ContentsAsync(app);

        var result = await SeedAsync(app);

        Assert.Equal(new DevSeedResult(0, 0, 0), result);
        Assert.Equal(before, await ContentsAsync(app));
    }

    [Fact]
    public async Task SeedingLeavesExistingUsersProductsAndBarcodesAlone()
    {
        await using var app = await StockroomApiFactory.CreateAsync(postgres);
        var paper = TestProducts.New(DevSeedCatalogue.Products[0].Sku);
        var other = TestProducts.New("OURS-1");
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var admin = new User { UserName = DevSeeder.Admin.Username, DisplayName = "Already here" };
            Assert.True((await users.CreateAsync(admin, TestAuth.Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(admin, Roles.Staff)).Succeeded);

            // The second catalogue product's only barcode, already on a product of ours.
            var db = scope.ServiceProvider.GetRequiredService<StockroomDbContext>();
            other.Barcodes.Add(TestProducts.Barcode(other.Id, DevSeedCatalogue.Barcode(1, 0)));
            db.Products.AddRange(paper, other);
            await db.SaveChangesAsync(Token);
        }

        var result = await SeedAsync(app);

        Assert.Equal(1, result.UsersCreated);
        Assert.Equal(DevSeedCatalogue.Products.Count - 1, result.ProductsCreated);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StockroomDbContext>();
            var admin = await db.Users.SingleAsync(u => u.UserName == DevSeeder.Admin.Username, Token);
            Assert.Equal("Already here", admin.DisplayName);
            Assert.Equal(paper.Name, await db.Products.Where(p => p.Id == paper.Id).Select(p => p.Name).SingleAsync(Token));
            Assert.False(await db.StockMovements.AnyAsync(m => m.ProductId == paper.Id, Token));
            Assert.Empty(await db.Products.Where(p => p.Sku == DevSeedCatalogue.Products[1].Sku).SelectMany(p => p.Barcodes).ToListAsync(Token));
        }
    }

    [Fact]
    public void CatalogueSkusAndBarcodesAreUniqueValidEan13s()
    {
        var barcodes = DevSeedCatalogue.Products
            .SelectMany((p, i) => Enumerable.Range(0, p.Barcodes).Select(b => DevSeedCatalogue.Barcode(i, b)))
            .ToList();

        Assert.Equal(DevSeedCatalogue.Products.Count, DevSeedCatalogue.Products.Select(p => p.Sku).Distinct().Count());
        Assert.Equal(barcodes.Count, barcodes.Distinct().Count());
        Assert.All(barcodes, b => Assert.Matches("^599[0-9]{10}$", b));
        Assert.True(IsValidEan13("4006381333931"));
        Assert.False(IsValidEan13("4006381333932"));
        Assert.All(barcodes, b => Assert.True(IsValidEan13(b), b));
    }

    [Fact]
    public async Task TheSeedCommandIsIdempotent()
    {
        var settings = StockroomApiFactory.SettingsFor(await postgres.CreateDatabaseAsync(Token));

        var first = await ApiProcess.RunAsync(settings, Token, "Development", DevSeedCommand.Name);
        var second = await ApiProcess.RunAsync(settings, Token, "Development", DevSeedCommand.Name);

        Assert.True(first.ExitCode == 0, first.StandardError);
        Assert.Contains($"created 2 users, {DevSeedCatalogue.Products.Count} products, and ", first.StandardOutput, StringComparison.Ordinal);
        Assert.Contains($"Sign in as admin / {DevSeeder.Admin.Password} (ADMIN).", first.StandardOutput, StringComparison.Ordinal);
        Assert.True(second.ExitCode == 0, second.StandardError);
        Assert.Contains("created 0 users, 0 products, and 0 stock movements.", second.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSeedCommandRefusesToRunOutsideDevelopment()
    {
        // The database URL points nowhere: the command must stop before touching it.
        var result = await ApiProcess.RunAsync(StockroomApiFactory.ValidSettings, Token, "Production", DevSeedCommand.Name);

        Assert.Equal(1, result.ExitCode);
        Assert.StartsWith("The seed command only runs in the Development environment", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", result.StandardError + result.StandardOutput, StringComparison.Ordinal);
    }

    private sealed record Contents(int Users, int Products, int Barcodes, int Movements, long Units);

    private static async Task<DevSeedResult> SeedAsync(StockroomApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DevSeeder>().SeedAsync(Token);
    }

    private static async Task<Contents> ContentsAsync(StockroomApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StockroomDbContext>();
        return new Contents(
            await db.Users.CountAsync(Token),
            await db.Products.CountAsync(Token),
            await db.ProductBarcodes.CountAsync(Token),
            await db.StockMovements.CountAsync(Token),
            await db.StockLevels.SumAsync(l => (long)l.Quantity, Token));
    }

    private static async Task<JsonDocument> GetAsAsync(HttpClient client, DevSeedUser user, string path)
    {
        using var login = await client.PostAsJsonAsync(TestAuth.LoginUri, new { username = user.Username, password = user.Password }, Token);
        login.EnsureSuccessStatusCode();
        var tokens = await TestAuth.ReadTokensAsync(login);

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        using var response = await client.SendAsync(request, Token);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
    }

    /// <summary>Weighted 1, 3, 1, … from the left, the digits of a valid EAN-13 sum to a multiple of 10.</summary>
    private static bool IsValidEan13(string barcode) =>
        barcode.Select((c, i) => (c - '0') * (i % 2 == 0 ? 1 : 3)).Sum() % 10 == 0;
}
