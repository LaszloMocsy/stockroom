using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Stockroom.Core.Identifiers;

namespace Stockroom.Tests.Api;

public sealed class HealthzTests(StockroomApiFactory factory) : IClassFixture<StockroomApiFactory>
{
    [Fact]
    public async Task GetHealthzReturnsOk()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/healthz", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

public sealed class ErrorEnvelopeWiringTests(StockroomApiFactory factory) : IClassFixture<StockroomApiFactory>
{
    [Fact]
    public async Task TheRealApplicationReturnsTheErrorEnvelope()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/no-such-route", UriKind.Relative), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("""{"error":{"code":"not_found","message":"Not Found","details":null}}""", body);
    }
}

public sealed class CoreServicesWiringTests(StockroomApiFactory factory) : IClassFixture<StockroomApiFactory>
{
    [Fact]
    public void TheRealApplicationProvidesTheClockAndIdGenerator()
    {
        Assert.Same(TimeProvider.System, factory.Services.GetRequiredService<TimeProvider>());
        Assert.NotNull(factory.Services.GetRequiredService<IIdGenerator>());
    }
}
