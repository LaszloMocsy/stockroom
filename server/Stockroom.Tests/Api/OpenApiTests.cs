using System.Net;
using System.Text.Json;
using Stockroom.Tests.Infrastructure;

namespace Stockroom.Tests.Api;

public sealed class OpenApiTests(StockroomApiFactory factory, PostgresFixture postgres) : IClassFixture<StockroomApiFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DocumentIsServedAsOpenApi31AndListsHealthz()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/openapi.json", UriKind.Relative), Token);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        var root = document.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("3.1.", root.GetProperty("openapi").GetString(), StringComparison.Ordinal);
        Assert.Equal("Stockroom API", root.GetProperty("info").GetProperty("title").GetString());
        Assert.Equal("v1", root.GetProperty("info").GetProperty("version").GetString());

        var healthz = root.GetProperty("paths").GetProperty("/healthz").GetProperty("get");
        Assert.Equal("GetHealthz", healthz.GetProperty("operationId").GetString());
        Assert.True(healthz.GetProperty("responses").TryGetProperty("200", out _));
    }

    [Fact]
    public async Task DocumentationEndpointsAreNotPartOfTheDocument()
    {
        using var client = factory.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync(new Uri("/api/v1/openapi.json", UriKind.Relative), Token));
        var paths = document.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();

        Assert.DoesNotContain(paths, p => p.StartsWith("/api/docs", StringComparison.Ordinal) || p.Contains("openapi", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Testing")]
    public async Task ReferenceUiIsNotServedOutsideDevelopment(string environment)
    {
        await using var app = await StockroomApiFactory.CreateAsync(postgres, environment);
        using var client = app.CreateClient();

        using var docs = await client.GetAsync(new Uri("/api/docs/", UriKind.Relative), Token);
        using var document = await client.GetAsync(new Uri("/api/v1/openapi.json", UriKind.Relative), Token);

        Assert.Equal(HttpStatusCode.NotFound, docs.StatusCode);
        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
    }

    [Fact]
    public async Task ReferenceUiIsServedAtApiDocsInDevelopmentAndPointsAtTheDocument()
    {
        await using var app = await StockroomApiFactory.CreateAsync(postgres, "Development");
        using var client = app.CreateClient();

        // /api/docs redirects to /api/docs/, which the client follows.
        using var response = await client.GetAsync(new Uri("/api/docs", UriKind.Relative), Token);
        var html = await response.Content.ReadAsStringAsync(Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/api/docs/", response.RequestMessage?.RequestUri?.AbsolutePath);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("\"url\":\"api/v1/openapi.json\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReferenceUiAssetsAreServedLocally()
    {
        // No CDN: the UI must work on a development machine without internet access.
        await using var app = await StockroomApiFactory.CreateAsync(postgres, "Development");
        using var client = app.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/docs/scalar.js", UriKind.Relative), Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
    }
}
