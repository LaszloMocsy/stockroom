using Microsoft.Extensions.Options;
using Stockroom.Api.Configuration;
using Stockroom.Api.Logging;

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Logging.AddStockroomLogging();
    builder.Services.AddStockroomOptions();

    var app = builder.Build();

    // Fail fast with a readable message. ValidateOnStart would catch this too, but only after
    // the host has logged the failure with a stack trace.
    _ = app.Services.GetRequiredService<IOptions<StockroomOptions>>().Value;

    app.UseStockroomRequestLogging();

    // Liveness: the process is up and serving requests. Dependency checks belong in /readyz.
    app.MapGet("/healthz", () => TypedResults.Ok());

    app.Run();
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
