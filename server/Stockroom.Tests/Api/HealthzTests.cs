using System.Net;

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
