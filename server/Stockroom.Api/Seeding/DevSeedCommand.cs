namespace Stockroom.Api.Seeding;

/// <summary>
/// <c>dotnet run --project Stockroom.Api -- seed</c>: migrates the database, fills it with development data
/// (see <see cref="DevSeeder"/>), and exits instead of serving requests.
/// </summary>
internal static class DevSeedCommand
{
    public const string Name = "seed";

    public static bool IsRequested(string[] args) => args is [Name];

    /// <summary>Runs the seed and returns the process exit code.</summary>
    public static async Task<int> RunAsync(WebApplication app)
    {
        // The seed users have well-known passwords, so a production database must never get them.
        if (!app.Environment.IsDevelopment())
        {
            Console.Error.WriteLine($"The {Name} command only runs in the Development environment, because it creates users with well-known passwords.");
            Console.Error.WriteLine($"  Current environment: {app.Environment.EnvironmentName}. Set ASPNETCORE_ENVIRONMENT=Development to seed a development database.");
            return 1;
        }

        await app.MigrateDatabaseAsync();

        DevSeedResult result;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            result = await scope.ServiceProvider.GetRequiredService<DevSeeder>().SeedAsync(app.Lifetime.ApplicationStopping);
        }

        Console.WriteLine($"Seeded the database: created {result.UsersCreated} users, {result.ProductsCreated} products, and {result.MovementsCreated} stock movements.");
        foreach (var user in new[] { DevSeeder.Admin, DevSeeder.Staff })
        {
            Console.WriteLine($"  Sign in as {user.Username} / {user.Password} ({user.Role}).");
        }

        return 0;
    }
}
