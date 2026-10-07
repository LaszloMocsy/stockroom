using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Stockroom.Api.Configuration;
using Stockroom.Tests.Infrastructure;

namespace Stockroom.Tests.Api;

/// <summary>
/// Hosts the API in-process with a valid set of <c>STOCKROOM_*</c> settings and its own empty
/// PostgreSQL database, which the API migrates on startup.
/// Runs in the "Testing" environment by default, so appsettings.Development.json does not apply.
/// </summary>
/// <remarks>
/// As a class fixture, xUnit creates the database through <see cref="InitializeAsync"/>; elsewhere,
/// use <see cref="CreateAsync"/>.
/// </remarks>
public sealed class StockroomApiFactory(PostgresFixture postgres) : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>
    /// Valid settings for hosts that never touch the database (options binding, startup failures that
    /// happen before migration). The database URL points nowhere; use <see cref="SettingsFor"/> otherwise.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string?> ValidSettings = new Dictionary<string, string?>
    {
        [StockroomOptions.DatabaseUrlKey] = "Host=localhost;Database=stockroom_test;Username=stockroom;Password=stockroom",
        [StockroomOptions.PublicUrlKey] = "https://stock.example.test",
    };

    private string _environment = "Testing";
    private Action<Dictionary<string, string?>>? _configure;

    /// <summary>The settings this factory starts the API with, after <see cref="InitializeAsync"/>.</summary>
    public IReadOnlyDictionary<string, string?> Settings { get; private set; } = ValidSettings;

    /// <summary>Valid settings using the database at <paramref name="databaseUrl"/>.</summary>
    public static Dictionary<string, string?> SettingsFor(string databaseUrl) =>
        new(ValidSettings) { [StockroomOptions.DatabaseUrlKey] = databaseUrl };

    /// <summary>
    /// Creates a factory with a new database, running in <paramref name="environment"/> and with any
    /// setting changes made by <paramref name="configure"/>.
    /// </summary>
    public static async Task<StockroomApiFactory> CreateAsync(
        PostgresFixture postgres,
        string environment = "Testing",
        Action<Dictionary<string, string?>>? configure = null)
    {
        var factory = new StockroomApiFactory(postgres) { _environment = environment, _configure = configure };
        await factory.InitializeAsync();
        return factory;
    }

    public async ValueTask InitializeAsync()
    {
        var settings = SettingsFor(await postgres.CreateDatabaseAsync(TestContext.Current.CancellationToken));
        _configure?.Invoke(settings);
        Settings = settings;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        foreach (var (key, value) in Settings)
        {
            builder.UseSetting(key, value);
        }
    }
}
