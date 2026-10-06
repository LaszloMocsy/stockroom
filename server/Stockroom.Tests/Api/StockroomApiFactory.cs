using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Stockroom.Api.Configuration;

namespace Stockroom.Tests.Api;

/// <summary>
/// Hosts the API in-process with a valid set of <c>STOCKROOM_*</c> settings.
/// Runs in the "Testing" environment so appsettings.Development.json does not apply.
/// </summary>
public sealed class StockroomApiFactory : WebApplicationFactory<Program>
{
    public static readonly IReadOnlyDictionary<string, string?> ValidSettings = new Dictionary<string, string?>
    {
        [StockroomOptions.DatabaseUrlKey] = "Host=localhost;Database=stockroom_test;Username=stockroom;Password=stockroom",
        [StockroomOptions.PublicUrlKey] = "https://stock.example.test",
    };

    private readonly IReadOnlyDictionary<string, string?> _settings;

    public StockroomApiFactory()
        : this(ValidSettings)
    {
    }

    private StockroomApiFactory(IReadOnlyDictionary<string, string?> settings) => _settings = settings;

    /// <summary>Creates a factory using exactly <paramref name="settings"/> instead of the valid defaults.</summary>
    public static StockroomApiFactory WithSettings(IReadOnlyDictionary<string, string?> settings) => new(settings);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }
    }
}
