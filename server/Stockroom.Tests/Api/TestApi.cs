using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stockroom.Api;

namespace Stockroom.Tests.Api;

/// <summary>
/// The real API composition (<c>AddStockroomApi</c> / <c>UseStockroomApi</c>) hosted in memory around
/// test-only endpoints, with every log entry captured.
/// </summary>
public sealed class TestApi : IAsyncDisposable
{
    private readonly WebApplication _app;

    private TestApi(WebApplication app, CapturingLoggerProvider logs)
    {
        _app = app;
        Logs = logs;
        Client = app.GetTestClient();
    }

    public HttpClient Client { get; }

    public CapturingLoggerProvider Logs { get; }

    public IServiceProvider Services => _app.Services;

    /// <param name="mapEndpoints">Maps the test endpoints.</param>
    /// <param name="settings">Settings added to, or replacing, <see cref="StockroomApiFactory.ValidSettings"/>.</param>
    public static async Task<TestApi> StartAsync(Action<IEndpointRouteBuilder> mapEndpoints, IReadOnlyDictionary<string, string?>? settings = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(StockroomApiFactory.ValidSettings);
        builder.Configuration.AddInMemoryCollection(settings ?? new Dictionary<string, string?>());
        builder.AddStockroomApi();

        // The validation source generator only discovers types in the assembly that calls AddValidation,
        // so test endpoints need this call here (production endpoints live next to the call in Stockroom.Api).
        builder.Services.AddValidation();

        var logs = new CapturingLoggerProvider();
        builder.Logging.AddProvider(logs);

        var app = builder.Build();
        app.UseStockroomApi();
        mapEndpoints(app);
        await app.StartAsync();
        return new TestApi(app, logs);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
    }
}

public sealed record CapturedLog(string Category, LogLevel Level, EventId EventId, string Message, Exception? Exception);

public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLog> _entries = new();

    public IReadOnlyList<CapturedLog> Entries => [.. _entries];

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class Logger(string category, ConcurrentQueue<CapturedLog> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue(new CapturedLog(category, logLevel, eventId, formatter(state, exception), exception));
    }
}
