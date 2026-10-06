var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Liveness: the process is up and serving requests. Dependency checks belong in /readyz.
app.MapGet("/healthz", () => TypedResults.Ok());

app.Run();
