using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Stockroom.Api.Configuration;

namespace Stockroom.Tests.Api;

/// <summary>
/// Hosts the API in-process with a valid set of <c>STOCKROOM_*</c> settings.
/// Runs in the "Testing" environment by default, so appsettings.Development.json does not apply.
/// </summary>
public sealed class StockroomApiFactory : WebApplicationFactory<Program>
{
    public static readonly IReadOnlyDictionary<string, string?> ValidSettings = new Dictionary<string, string?>
    {
        [StockroomOptions.DatabaseUrlKey] = "Host=localhost;Database=stockroom_test;Username=stockroom;Password=stockroom",
        [StockroomOptions.PublicUrlKey] = "https://stock.example.test",
    };

    private readonly IReadOnlyDictionary<string, string?> _settings;
    private readonly string _environment;

    public StockroomApiFactory()
        : this(ValidSettings, "Testing")
    {
    }

    private StockroomApiFactory(IReadOnlyDictionary<string, string?> settings, string environment)
    {
        _settings = settings;
        _environment = environment;
    }

    /// <summary>Creates a factory using exactly <paramref name="settings"/> instead of the valid defaults.</summary>
    public static StockroomApiFactory WithSettings(IReadOnlyDictionary<string, string?> settings) => new(settings, "Testing");

    /// <summary>Creates a factory with the valid settings running in <paramref name="environment"/>, e.g. "Development".</summary>
    public static StockroomApiFactory WithEnvironment(string environment) => new(ValidSettings, environment);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }
    }
}
