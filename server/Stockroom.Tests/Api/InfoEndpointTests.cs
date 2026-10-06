using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;

namespace Stockroom.Tests.Api;

public sealed partial class InfoEndpointTests(StockroomApiFactory factory) : IClassFixture<StockroomApiFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task InfoReturnsTheVersionsAndSetupState()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/info", UriKind.Relative), Token);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        var info = document.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(["server_version", "api_version", "min_client_version", "setup_required"], info.EnumerateObject().Select(p => p.Name));
        Assert.Equal("1.0", info.GetProperty("api_version").GetString());
        Assert.Equal("0.0.0", info.GetProperty("min_client_version").GetString());
        Assert.True(info.GetProperty("setup_required").GetBoolean());
    }

    [Fact]
    public async Task ServerVersionIsTheBuildVersionWithoutBuildMetadata()
    {
        using var client = factory.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync(new Uri("/api/v1/info", UriKind.Relative), Token));
        var serverVersion = document.RootElement.GetProperty("server_version").GetString()!;

        // Semantic version with an optional pre-release, but no "+commit" suffix.
        Assert.Matches(SemanticVersion(), serverVersion);
        Assert.StartsWith(typeof(Stockroom.Api.Endpoints.InfoEndpoints).Assembly.GetName().Version!.ToString(3), serverVersion, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InfoIsPublic()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/info", UriKind.Relative), Token);
        var endpoint = factory.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == "/api/v1/info");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>());
    }

    [Fact]
    public async Task InfoIsDescribedInTheOpenApiDocument()
    {
        using var client = factory.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync(new Uri("/api/v1/openapi.json", UriKind.Relative), Token));
        var root = document.RootElement;
        var operation = root.GetProperty("paths").GetProperty("/api/v1/info").GetProperty("get");
        var schema = root.GetProperty("components").GetProperty("schemas").GetProperty("InfoResponse");

        Assert.Equal("GetInfo", operation.GetProperty("operationId").GetString());
        Assert.Equal(
            ["server_version", "api_version", "min_client_version", "setup_required"],
            schema.GetProperty("properties").EnumerateObject().Select(p => p.Name));
        Assert.Equal(4, schema.GetProperty("required").GetArrayLength());
    }

    [GeneratedRegex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$")]
    private static partial Regex SemanticVersion();
}
