using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Stockroom.Api.Auth;
using Stockroom.Api.Configuration;
using Stockroom.Tests.Infrastructure;

namespace Stockroom.Tests.Api;

/// <summary>Strict CORS from <c>STOCKROOM_ALLOWED_CORS_ORIGINS</c>, empty by default (spec 11).</summary>
public sealed class CorsTests(PostgresFixture postgres)
{
    private const string AllowedOrigins = "https://app.example.test,http://localhost:5173";

    private static readonly Uri InfoUri = new("/api/v1/info", UriKind.Relative);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("https://app.example.test")]
    [InlineData("http://localhost:5173")]
    public async Task AllowedOriginsCanReadResponses(string origin)
    {
        await using var app = await CreateAsync(AllowedOrigins);
        using var client = app.CreateClient();

        using var response = await client.SendAsync(Get(InfoUri, origin), Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([origin], Header(response, "Access-Control-Allow-Origin"));
        Assert.Contains("Origin", response.Headers.Vary);

        // Tokens travel in headers and bodies, never in cookies, so credentials stay disallowed.
        Assert.Empty(Header(response, "Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task PreflightFromAnAllowedOriginIsAnsweredWithoutAuthentication()
    {
        await using var app = await CreateAsync(AllowedOrigins);
        using var client = app.CreateClient();

        // PATCH /users/{id} needs an ADMIN, but browsers send preflights without the Authorization header.
        using var response = await client.SendAsync(Preflight(new Uri($"/api/v1/users/{Guid.NewGuid()}", UriKind.Relative), "https://app.example.test", "PATCH"), Token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["https://app.example.test"], Header(response, "Access-Control-Allow-Origin"));
        Assert.Equal(["PATCH"], Header(response, "Access-Control-Allow-Methods"));
        Assert.Equal(["authorization,content-type,idempotency-key,x-client-version,x-client-platform"], Header(response, "Access-Control-Allow-Headers"));
        Assert.Equal(["600"], Header(response, "Access-Control-Max-Age"));
    }

    [Theory]
    [InlineData("https://evil.example.test")]
    [InlineData("http://app.example.test")]
    [InlineData("https://app.example.test:8443")]
    [InlineData("https://sub.app.example.test")]
    [InlineData("null")]
    public async Task OtherOriginsGetNoCorsHeaders(string origin)
    {
        await using var app = await CreateAsync(AllowedOrigins);
        using var client = app.CreateClient();

        using var get = await client.SendAsync(Get(InfoUri, origin), Token);
        using var preflight = await client.SendAsync(Preflight(InfoUri, origin, "GET"), Token);

        // The request itself still runs, as CORS is enforced by the browser: it hides the response.
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        AssertNoCorsHeaders(get);
        AssertNoCorsHeaders(preflight);
    }

    [Fact]
    public async Task NoOriginIsAllowedByDefault()
    {
        await using var app = await CreateAsync(allowedOrigins: null);
        using var client = app.CreateClient();

        using var get = await client.SendAsync(Get(InfoUri, "https://app.example.test"), Token);
        using var preflight = await client.SendAsync(Preflight(InfoUri, "https://app.example.test", "GET"), Token);

        Assert.Empty(app.Services.GetRequiredService<IOptions<StockroomOptions>>().Value.AllowedCorsOrigins);
        AssertNoCorsHeaders(get);
        AssertNoCorsHeaders(preflight);
    }

    [Fact]
    public async Task UnauthorizedResponsesAreReadableByAllowedOrigins()
    {
        await using var app = await CreateAsync(AllowedOrigins);
        using var client = app.CreateClient();

        // So the web app can tell an expired access token apart from a network failure.
        using var response = await client.SendAsync(Get(new Uri("/api/v1/me", UriKind.Relative), "https://app.example.test"), Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(["https://app.example.test"], Header(response, "Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task RateLimitedResponsesExposeRetryAfter()
    {
        await using var factory = await CreateAsync(AllowedOrigins);
        await using var app = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.Configure<AuthRateLimitOptions>(o => o.PermitLimit = 1)));
        using var client = app.CreateClient();

        (await client.SendAsync(Login("https://app.example.test"), Token)).Dispose();
        using var response = await client.SendAsync(Login("https://app.example.test"), Token);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.NotNull(response.Headers.RetryAfter);
        Assert.Equal(["https://app.example.test"], Header(response, "Access-Control-Allow-Origin"));
        Assert.Equal(["Retry-After"], Header(response, "Access-Control-Expose-Headers"));
    }

    [Fact]
    public async Task ServerErrorsAreReadableByAllowedOrigins()
    {
        await using var api = await TestApi.StartAsync(
            app => app.MapGet("/boom", (Func<string>)(() => throw new InvalidOperationException("boom"))),
            new Dictionary<string, string?> { [StockroomOptions.AllowedCorsOriginsKey] = AllowedOrigins });

        using var response = await api.Client.SendAsync(Get(new Uri("/boom", UriKind.Relative), "https://app.example.test"), Token);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(["https://app.example.test"], Header(response, "Access-Control-Allow-Origin"));
    }

    private Task<StockroomApiFactory> CreateAsync(string? allowedOrigins) =>
        StockroomApiFactory.CreateAsync(postgres, configure: settings => settings[StockroomOptions.AllowedCorsOriginsKey] = allowedOrigins);

    private static HttpRequestMessage Get(Uri uri, string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Add("Origin", origin);
        return request;
    }

    private static HttpRequestMessage Preflight(Uri uri, string origin, string method)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, uri);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", method);
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type,idempotency-key,x-client-version,x-client-platform");
        return request;
    }

    private static HttpRequestMessage Login(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, TestAuth.LoginUri)
        {
            Content = JsonContent.Create(new { username = "nobody", password = "wrong passphrase" }),
        };
        request.Headers.Add("Origin", origin);
        return request;
    }

    private static IEnumerable<string> Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values : [];

    private static void AssertNoCorsHeaders(HttpResponseMessage response) =>
        Assert.DoesNotContain(response.Headers, h => h.Key.StartsWith("Access-Control-", StringComparison.OrdinalIgnoreCase));
}
