using Microsoft.Extensions.Options;
using Stockroom.Api;
using Stockroom.Api.Configuration;
using Stockroom.Api.Endpoints;
using Stockroom.Api.OpenApi;
using Stockroom.Data;

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.AddStockroomApi();

    var app = builder.Build();

    // Fail fast with a readable message. ValidateOnStart would catch this too, but only after
    // the host has logged the failure with a stack trace.
    _ = app.Services.GetRequiredService<IOptions<StockroomOptions>>().Value;

    await app.MigrateDatabaseAsync();

    app.UseStockroomApi();

    app.MapStockroomOpenApi();

    // Liveness: the process is up and serving requests. Dependency checks belong in /readyz.
    app.MapGet("/healthz", () => TypedResults.Ok())
        .WithName("GetHealthz")
        .WithTags("Health")
        .WithSummary("Liveness probe")
        .WithDescription("Returns 200 while the process is up and serving requests. Does not check dependencies.");

    var v1 = app.MapGroup("/api/v1");
    v1.MapInfoEndpoints();
    v1.MapSetupEndpoints();

    await app.RunAsync();
    return 0;
}
catch (OptionsValidationException ex) when (ex.OptionsType == typeof(StockroomOptions))
{
    // Logging may not be configured yet (it depends on these settings), so write to stderr directly.
    Console.Error.WriteLine("Stockroom cannot start because its configuration is invalid:");
    foreach (var failure in ex.Failures)
    {
        Console.Error.WriteLine($"  - {failure}");
    }

    return 1;
}
catch (DatabaseSchemaTooNewException ex)
{
    // Migrations are forward-only; the operator has to run a server at least as new as the database.
    Console.Error.WriteLine("Stockroom cannot start because the database was created or upgraded by a newer version of Stockroom.");
    Console.Error.WriteLine($"  Migrations unknown to this version: {string.Join(", ", ex.UnknownMigrations)}");
    Console.Error.WriteLine("  Run the newer version, or restore a backup taken before the upgrade.");
    return 1;
}
