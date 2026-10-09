using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stockroom.Api.Configuration;
using Stockroom.Tests.Infrastructure;

namespace Stockroom.Tests.Api;

public sealed class StockroomOptionsTests(PostgresFixture postgres)
{
    [Fact]
    public async Task StartupFailsNamingEveryMissingRequiredSetting()
    {
        var result = await ApiProcess.RunAsync(new Dictionary<string, string?>(), TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        Assert.StartsWith("Stockroom cannot start because its configuration is invalid:", result.StandardError, StringComparison.Ordinal);
        Assert.Contains($"  - {StockroomOptions.DatabaseUrlKey} is required", result.StandardError, StringComparison.Ordinal);
        Assert.Contains($"  - {StockroomOptions.PublicUrlKey} is required", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", result.StandardError + result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartupFailsOnInvalidValues()
    {
        var settings = new Dictionary<string, string?>(StockroomApiFactory.ValidSettings)
        {
            [StockroomOptions.PublicUrlKey] = "stock.example.test",
            [StockroomOptions.AllowedCorsOriginsKey] = "https://ok.example.test, https://bad.example.test/path",
            [StockroomOptions.LogLevelKey] = "Loud",
            [StockroomOptions.UndoWindowSecondsKey] = "-5",
        };

        var result = await ApiProcess.RunAsync(settings, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains($"{StockroomOptions.PublicUrlKey} must be an absolute http or https URL, got 'stock.example.test'", result.StandardError, StringComparison.Ordinal);
        Assert.Contains($"{StockroomOptions.AllowedCorsOriginsKey} must be a comma-separated list of origins", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("got 'https://bad.example.test/path'", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("got 'https://ok.example.test'", result.StandardError, StringComparison.Ordinal);
        Assert.Contains($"{StockroomOptions.LogLevelKey} must be one of", result.StandardError, StringComparison.Ordinal);
        Assert.Contains($"{StockroomOptions.UndoWindowSecondsKey} must be a whole number of seconds, 0 or more, got '-5'", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain(StockroomOptions.DatabaseUrlKey, result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidSettingsAreBound()
    {
        await using var factory = await StockroomApiFactory.CreateAsync(postgres, configure: settings =>
        {
            settings[StockroomOptions.AllowedCorsOriginsKey] = " https://app.example.test , http://localhost:5173/ ,";
            settings[StockroomOptions.LogLevelKey] = "warning";
            settings[StockroomOptions.UndoWindowSecondsKey] = "90";
        });

        var options = factory.Services.GetRequiredService<IOptions<StockroomOptions>>().Value;

        Assert.Equal(factory.Settings[StockroomOptions.DatabaseUrlKey], options.DatabaseUrl);
        Assert.Equal(new Uri("https://stock.example.test"), options.PublicUrl);
        Assert.Equal(["https://app.example.test", "http://localhost:5173"], options.AllowedCorsOrigins);
        Assert.Equal(LogLevel.Warning, options.LogLevel);
        Assert.Equal(TimeSpan.FromSeconds(90), options.UndoWindow);
    }

    [Fact]
    public async Task OptionalSettingsHaveDefaults()
    {
        await using var factory = await StockroomApiFactory.CreateAsync(postgres);

        var options = factory.Services.GetRequiredService<IOptions<StockroomOptions>>().Value;

        Assert.Empty(options.AllowedCorsOrigins);
        Assert.Equal(LogLevel.Information, options.LogLevel);
        Assert.Equal(TimeSpan.FromMinutes(5), options.UndoWindow);
    }

    [Fact]
    public async Task LogLevelSettingOverridesTheDefaultLogLevel()
    {
        await using var factory = await StockroomApiFactory.CreateAsync(postgres, configure: settings =>
            settings[StockroomOptions.LogLevelKey] = "Error");

        var logger = factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Stockroom.Test");

        Assert.False(logger.IsEnabled(LogLevel.Warning));
        Assert.True(logger.IsEnabled(LogLevel.Error));
    }
}
