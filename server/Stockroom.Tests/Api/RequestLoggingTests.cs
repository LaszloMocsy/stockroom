using System.Globalization;
using System.Net;
using System.Text.Json;
using Stockroom.Api.Configuration;
using Stockroom.Tests.Infrastructure;

namespace Stockroom.Tests.Api;

public sealed class RequestLoggingFixture(PostgresFixture postgres) : IAsyncLifetime
{
    public ApiProcess.RunningApi Api { get; private set; } = null!;

    public HttpClient Client { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var token = TestContext.Current.CancellationToken;
        Api = await ApiProcess.StartAsync(StockroomApiFactory.SettingsFor(await postgres.CreateDatabaseAsync(token)), token);
        Client = new HttpClient { BaseAddress = Api.BaseAddress };
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await Api.DisposeAsync();
    }
}

public sealed class RequestLoggingTests(RequestLoggingFixture fixture) : IClassFixture<RequestLoggingFixture>
{
    private const string CorrelationHeader = "X-Correlation-Id";
    private const string RequestCategory = "Stockroom.Api.Logging.RequestLoggingMiddleware";

    // Health probes log at Debug, below the default level, so ordinary requests use an unmapped path.
    private const string OrdinaryPath = "/no-such-route";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RequestLogsOneJsonLineWithTheSuppliedCorrelationId()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/no-such-route?secret=hunter2");
        request.Headers.Add(CorrelationHeader, "test-supplied.id_1");

        using var response = await fixture.Client.SendAsync(request, Token);
        var line = await WaitForRequestLineAsync("test-supplied.id_1");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("test-supplied.id_1", Assert.Single(response.Headers.GetValues(CorrelationHeader)));
        Assert.Equal("GET", line.GetProperty("State").GetProperty("Method").GetString());
        Assert.Equal("/no-such-route", line.GetProperty("State").GetProperty("Path").GetString());
        Assert.Equal(404, line.GetProperty("State").GetProperty("StatusCode").GetInt32());
        Assert.Equal("Information", line.GetProperty("LogLevel").GetString());
        Assert.DoesNotContain("hunter2", line.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TimestampIsUtcIso8601()
    {
        using var response = await GetAsync(OrdinaryPath, correlationId: "timestamp-check");
        var line = await WaitForRequestLineAsync("timestamp-check");

        var timestamp = line.GetProperty("Timestamp").GetString()!;

        Assert.EndsWith("Z", timestamp, StringComparison.Ordinal);
        Assert.True(DateTimeOffset.TryParseExact(timestamp, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _), timestamp);
    }

    [Fact]
    public async Task MissingCorrelationIdIsGeneratedAndEchoed()
    {
        using var response = await GetAsync(OrdinaryPath);
        var generated = Assert.Single(response.Headers.GetValues(CorrelationHeader));

        var line = await WaitForRequestLineAsync(generated);

        Assert.Equal(32, generated.Length);
        Assert.Equal(generated, CorrelationIdOf(line));
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("semi;colon")]
    [InlineData("quote\"inside")]
    public async Task MalformedCorrelationIdIsReplaced(string supplied)
    {
        using var response = await GetAsync(OrdinaryPath, supplied);
        var replacement = Assert.Single(response.Headers.GetValues(CorrelationHeader));

        var line = await WaitForRequestLineAsync(replacement);

        Assert.NotEqual(supplied, replacement);
        Assert.Equal(replacement, CorrelationIdOf(line));
    }

    [Fact]
    public async Task OverlongCorrelationIdIsReplaced()
    {
        var supplied = new string('a', 65);

        using var response = await GetAsync(OrdinaryPath, supplied);

        Assert.NotEqual(supplied, Assert.Single(response.Headers.GetValues(CorrelationHeader)));
    }

    [Theory]
    [InlineData("/healthz")]
    [InlineData("/HEALTHZ/")]
    public async Task SuccessfulProbesAreNotLoggedAtTheDefaultLevel(string path)
    {
        var correlationId = $"probe-{Guid.NewGuid():N}";
        using var probe = await GetAsync(path, correlationId);
        // Log lines are written in order, so once a later request is logged the probe would have been too.
        var marker = $"marker-{Guid.NewGuid():N}";
        using var after = await GetAsync(OrdinaryPath, marker);
        await WaitForRequestLineAsync(marker);

        Assert.Equal(HttpStatusCode.OK, probe.StatusCode);
        Assert.DoesNotContain(fixture.Api.Lines, l => CorrelationIdOf(l) == correlationId);
    }

    [Fact]
    public async Task UnsuccessfulProbesAreLoggedNormally()
    {
        // /readyz is not mapped yet, so it answers 404: a failing probe must stay visible.
        using var response = await GetAsync("/readyz", "readyz-not-found");
        var line = await WaitForRequestLineAsync("readyz-not-found");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Information", line.GetProperty("LogLevel").GetString());
    }

    [Fact]
    public async Task EachRequestWritesExactlyOneLine()
    {
        using var first = await GetAsync(OrdinaryPath, "once-a");
        await WaitForRequestLineAsync("once-a");
        using var second = await GetAsync(OrdinaryPath, "once-b");
        await WaitForRequestLineAsync("once-b");

        var lines = fixture.Api.Lines;

        Assert.Single(lines, l => CorrelationIdOf(l) == "once-a");
        Assert.Single(lines, l => CorrelationIdOf(l) == "once-b");
        Assert.DoesNotContain(lines, l => l.GetProperty("Category").GetString()!.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }

    private Task<HttpResponseMessage> GetAsync(string path, string? correlationId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (correlationId is not null)
        {
            request.Headers.TryAddWithoutValidation(CorrelationHeader, correlationId);
        }

        return fixture.Client.SendAsync(request, Token);
    }

    private Task<JsonElement> WaitForRequestLineAsync(string correlationId) =>
        fixture.Api.WaitForLineAsync(l => l.GetProperty("Category").GetString() == RequestCategory && CorrelationIdOf(l) == correlationId, Token);

    private static string? CorrelationIdOf(JsonElement line)
    {
        foreach (var scope in line.GetProperty("Scopes").EnumerateArray())
        {
            if (scope.TryGetProperty("CorrelationId", out var id))
            {
                return id.GetString();
            }
        }

        return null;
    }
}

public sealed class ProbeLoggingAtDebugTests(PostgresFixture postgres)
{
    [Fact]
    public async Task SuccessfulProbesAreLoggedAtDebugWhenEnabled()
    {
        var token = TestContext.Current.CancellationToken;
        var settings = StockroomApiFactory.SettingsFor(await postgres.CreateDatabaseAsync(token));
        settings[StockroomOptions.LogLevelKey] = "Debug";
        await using var api = await ApiProcess.StartAsync(settings, token);
        using var client = new HttpClient { BaseAddress = api.BaseAddress };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/healthz");
        request.Headers.Add("X-Correlation-Id", "probe-at-debug");

        using var response = await client.SendAsync(request, token);
        var line = await api.WaitForLineAsync(
            l => l.GetProperty("Category").GetString() == "Stockroom.Api.Logging.RequestLoggingMiddleware"
                && l.GetRawText().Contains("\"CorrelationId\":\"probe-at-debug\"", StringComparison.Ordinal),
            token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Debug", line.GetProperty("LogLevel").GetString());
        Assert.Equal("/healthz", line.GetProperty("State").GetProperty("Path").GetString());
    }
}
