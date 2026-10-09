using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Stockroom.Tests.Infrastructure;

namespace Stockroom.Tests.Api;

/// <summary>
/// Removing barcodes through the real server (Kestrel), whose handling of encoded slashes in the path the
/// in-memory test server does not reproduce.
/// </summary>
public sealed class ProductBarcodeProcessTests(PostgresFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task BarcodesWithReservedCharactersRoundTrip()
    {
        await using var api = await ApiProcess.StartAsync(StockroomApiFactory.SettingsFor(await postgres.CreateDatabaseAsync(Token)), Token);
        using var client = new HttpClient { BaseAddress = api.BaseAddress };
        using var setup = await client.PostAsJsonAsync("/api/v1/setup", new { username = "admin", display_name = "Admin", password = TestAuth.Password }, Token);
        setup.EnsureSuccessStatusCode();
        using var login = await client.PostAsJsonAsync(TestAuth.LoginUri, new { username = "admin", password = TestAuth.Password }, Token);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await TestAuth.ReadTokensAsync(login)).AccessToken);
        string[] barcodes = ["https://example.com/p/42?lot=7&size=10%25", "A/B C+D#E", "%2F literally", "100%", "kept"];
        using var created = await client.PostAsJsonAsync("/api/v1/products", new { name = "QR labelled" }, Token);
        var id = (await ReadAsync(created)).GetProperty("id").GetGuid();
        foreach (var barcode in barcodes)
        {
            using var added = await client.PostAsJsonAsync($"/api/v1/products/{id}/barcodes", new { barcode }, Token);
            Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        }

        var remaining = new List<string>(barcodes);
        foreach (var barcode in barcodes[..^1])
        {
            using var response = await client.DeleteAsync($"/api/v1/products/{id}/barcodes/{Uri.EscapeDataString(barcode)}", Token);
            remaining.Remove(barcode);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(remaining, (await ReadAsync(response)).GetProperty("barcodes").EnumerateArray().Select(b => b.GetString()));
        }
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return document.RootElement.Clone();
    }
}
