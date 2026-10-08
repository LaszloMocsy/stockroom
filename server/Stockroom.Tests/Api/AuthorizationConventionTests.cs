using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Stockroom.Tests.Infrastructure;

namespace Stockroom.Tests.Api;

/// <summary>
/// Every endpoint requires a logged-in user unless it is deliberately public (spec 10.2, 11). These tests
/// fail for an endpoint mapped outside Program.cs's authorised group, or newly marked AllowAnonymous.
/// </summary>
public sealed partial class AuthorizationConventionTests(PostgresFixture postgres)
{
    // Routes anyone may call without logging in. Adding one is a security decision: keep the list short.
    private static readonly HashSet<string> PublicRoutes =
    [
        "/healthz",
        "/api/{documentName}/openapi.json",
        "/api/v1/info",
        "/api/v1/setup",
        "/api/v1/auth/login",
        "/api/v1/auth/refresh",
        "/api/v1/auth/logout",

        // The API reference, mapped in the Development environment only.
        "/api/docs/{documentName?}",
        "/api/docs/scalar.js",
        "/api/docs/scalar.aspnetcore.js",
        "/api/docs/favicon.svg",
    ];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EveryEndpointRequiresAuthorizationUnlessListedAsPublic()
    {
        // Development maps the most endpoints (it adds the API reference).
        await using var app = await StockroomApiFactory.CreateAsync(postgres, "Development");

        var problems = FindUnprotected(RouteEndpoints(app.Services));

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public async Task EveryListedPublicRouteExists()
    {
        await using var app = await StockroomApiFactory.CreateAsync(postgres, "Development");

        var routes = RouteEndpoints(app.Services).Select(e => e.RoutePattern.RawText!).ToHashSet();

        Assert.All(PublicRoutes, route => Assert.Contains(route, routes));
    }

    [Fact]
    public async Task ProtectedEndpointsRejectAnonymousRequests()
    {
        await using var app = await StockroomApiFactory.CreateAsync(postgres, "Development");
        using var client = app.CreateClient();
        var protectedEndpoints = RouteEndpoints(app.Services).Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null).ToList();
        var problems = new List<string>();

        foreach (var endpoint in protectedEndpoints)
        {
            // Route parameters get a made-up public ID; authorisation runs before anything looks it up.
            var path = RouteParameter().Replace(endpoint.RoutePattern.RawText!, Guid.NewGuid().ToString());
            foreach (var method in endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), path);
                using var response = await client.SendAsync(request, Token);
                if (response.StatusCode != HttpStatusCode.Unauthorized)
                {
                    problems.Add($"{method} {path} answered an anonymous request with {(int)response.StatusCode}.");
                }
            }
        }

        Assert.NotEmpty(protectedEndpoints);
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public async Task EndpointsLeftAnonymousByMistakeAreReported()
    {
        await using var api = await TestApi.StartAsync(app =>
        {
            app.MapGroup("").RequireAuthorization().MapGet("/protected", () => "ok");
            app.MapGet("/forgotten", () => "ok");
            app.MapGet("/marked-public", () => "ok").AllowAnonymous();
        });

        var problems = FindUnprotected(RouteEndpoints(api.Services));

        Assert.Collection(
            problems.Order(),
            p => Assert.StartsWith("/forgotten ", p, StringComparison.Ordinal),
            p => Assert.StartsWith("/marked-public ", p, StringComparison.Ordinal));
    }

    private static List<RouteEndpoint> RouteEndpoints(IServiceProvider services) =>
        services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();

    private static List<string> FindUnprotected(IEnumerable<RouteEndpoint> endpoints)
    {
        var problems = new List<string>();
        foreach (var endpoint in endpoints)
        {
            var route = endpoint.RoutePattern.RawText;
            if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            {
                if (!PublicRoutes.Contains(route!))
                {
                    problems.Add($"{route} is AllowAnonymous but not listed in {nameof(PublicRoutes)}.");
                }
            }
            else if (endpoint.Metadata.GetMetadata<IAuthorizeData>() is null && endpoint.Metadata.GetMetadata<AuthorizationPolicy>() is null)
            {
                problems.Add($"{route} requires no authorisation. Map it inside the authorised group in Program.cs.");
            }
        }

        return problems;
    }

    [GeneratedRegex(@"\{[^}]*\}")]
    private static partial Regex RouteParameter();
}
