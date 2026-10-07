using System.Text.Json;
using Microsoft.Extensions.Options;
using Stockroom.Api.Configuration;
using Stockroom.Api.Errors;
using Stockroom.Api.Logging;
using Stockroom.Api.OpenApi;
using Stockroom.Core;
using Stockroom.Core.Settings;
using Stockroom.Data;
using Stockroom.Data.Settings;

namespace Stockroom.Api;

/// <summary>
/// The API's service and middleware composition, kept out of Program.cs so tests can build the
/// same pipeline around test-only endpoints.
/// </summary>
internal static class StockroomApiExtensions
{
    public static WebApplicationBuilder AddStockroomApi(this WebApplicationBuilder builder)
    {
        builder.Logging.AddStockroomLogging();
        builder.Services.AddStockroomOptions();
        builder.Services.AddStockroomCore();
        builder.Services.AddDbContext<StockroomDbContext>((services, options) =>
            options.UseStockroomDatabase(services.GetRequiredService<IOptions<StockroomOptions>>().Value.DatabaseUrl));
        builder.Services.AddScoped<ISettingsStore, SettingsStore>();

        // JSON field names are snake_case throughout the API (spec 10).
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower);

        builder.Services.AddStockroomErrorHandling();
        builder.Services.AddStockroomOpenApi();
        return builder;
    }

    public static WebApplication UseStockroomApi(this WebApplication app)
    {
        app.UseStockroomRequestLogging();
        app.UseStockroomErrorHandling();
        return app;
    }

    /// <summary>Applies pending migrations before the server accepts requests (see <see cref="DatabaseMigrator"/>).</summary>
    public static async Task MigrateDatabaseAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StockroomDbContext>();
        await DatabaseMigrator.MigrateAsync(db, app.Lifetime.ApplicationStopping);
    }
}
