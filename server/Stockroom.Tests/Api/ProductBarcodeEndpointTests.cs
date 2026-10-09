using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Stockroom.Api.Configuration;
using Stockroom.Core.Users;
using Stockroom.Tests.Infrastructure;
using static Stockroom.Tests.Api.TestAuth;

namespace Stockroom.Tests.Api;

/// <summary><c>POST /api/v1/products/:id/barcodes</c> and <c>DELETE /api/v1/products/:id/barcodes/:barcode</c>.</summary>
public sealed class ProductBarcodeEndpointTests(StockroomApiFactory factory) : IClassFixture<StockroomApiFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string DatabaseUrl => factory.Settings[StockroomOptions.DatabaseUrlKey]!;

    [Fact]
    public async Task StaffCanAttachABarcode()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var first = NewBarcode();
        var created = await CreateAsync(client, first);
        var id = created.GetProperty("id").GetGuid();
        var second = NewBarcode();

        using var response = await AddAsync(client, id, second);
        var body = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal([first, second], Barcodes(body));
        Assert.True(body.GetProperty("updated_at").GetDateTimeOffset() > created.GetProperty("updated_at").GetDateTimeOffset());
        Assert.Equal([first, second], await StoredBarcodesAsync(id));
    }

    [Fact]
    public async Task AttachingABarcodeTheProductHasChangesNothing()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var barcode = NewBarcode();
        var created = await CreateAsync(client, barcode);
        var id = created.GetProperty("id").GetGuid();

        using var response = await AddAsync(client, id, barcode);
        var body = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal([barcode], Barcodes(body));
        Assert.Equal(created.GetProperty("updated_at").GetDateTimeOffset(), body.GetProperty("updated_at").GetDateTimeOffset());
    }

    [Fact]
    public async Task ABarcodeOfAnotherProductIsAConflict()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var barcode = NewBarcode();
        var owner = (await CreateAsync(client, barcode)).GetProperty("id").GetGuid();
        var other = (await CreateAsync(client, barcode: null)).GetProperty("id").GetGuid();

        using var response = await AddAsync(client, other, barcode);
        var error = (await ReadAsync(response)).GetProperty("error");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("barcode_taken", error.GetProperty("code").GetString());
        Assert.Equal(barcode, error.GetProperty("details").GetProperty("barcode").GetString());
        Assert.Equal(owner, error.GetProperty("details").GetProperty("product_id").GetGuid());
        Assert.Empty(await StoredBarcodesAsync(other));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "barcode": " " }""")]
    [InlineData("""{ "barcode": "{long}" }""")]
    public async Task AnInvalidBarcodeIsRejected(string json)
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var id = (await CreateAsync(client, barcode: null)).GetProperty("id").GetGuid();

        using var content = new StringContent(json.Replace("{long}", new string('1', 513), StringComparison.Ordinal), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(BarcodesUri(id), content, Token);
        var error = (await ReadAsync(response)).GetProperty("error");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", error.GetProperty("code").GetString());
        Assert.Equal(["barcode"], error.GetProperty("details").GetProperty("fields").EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task AttachingToAnUnknownProductIsNotFound()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);

        using var response = await AddAsync(client, Guid.NewGuid(), NewBarcode());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("not_found", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task StaffCanRemoveABarcode()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var (kept, removed) = (NewBarcode(), NewBarcode());
        var created = await CreateAsync(client, kept);
        var id = created.GetProperty("id").GetGuid();
        using var added = await AddAsync(client, id, removed);

        using var response = await RemoveAsync(client, id, removed);
        var body = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([kept], Barcodes(body));
        Assert.True(body.GetProperty("updated_at").GetDateTimeOffset() > (await ReadAsync(added)).GetProperty("updated_at").GetDateTimeOffset());
        Assert.Equal([kept], await StoredBarcodesAsync(id));
    }

    [Fact]
    public async Task TheLastBarcodeCanBeRemovedAndThenUsedElsewhere()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var barcode = NewBarcode();
        var id = (await CreateAsync(client, barcode)).GetProperty("id").GetGuid();
        var other = (await CreateAsync(client, barcode: null)).GetProperty("id").GetGuid();

        using var response = await RemoveAsync(client, id, barcode);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(Barcodes(await ReadAsync(response)));
        Assert.Empty(await StoredBarcodesAsync(id));
        using var reattached = await AddAsync(client, other, barcode);
        Assert.Equal(HttpStatusCode.Created, reattached.StatusCode);
    }

    /// <summary>In memory; <see cref="ProductBarcodeProcessTests"/> covers the real server, including a literal "%2F".</summary>
    [Theory]
    [InlineData("https://example.com/p/42?lot=7&size=10%25")]
    [InlineData("A/B C+D#E")]
    public async Task BarcodesWithReservedCharactersCanBeRemoved(string payload)
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var barcode = $"{payload} {Guid.NewGuid():N}";
        var id = (await CreateAsync(client, barcode)).GetProperty("id").GetGuid();

        using var response = await RemoveAsync(client, id, barcode);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await StoredBarcodesAsync(id));
    }

    [Fact]
    public async Task RemovingABarcodeOfAnotherProductIsNotFound()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);
        var barcode = NewBarcode();
        var owner = (await CreateAsync(client, barcode)).GetProperty("id").GetGuid();
        var other = (await CreateAsync(client, barcode: null)).GetProperty("id").GetGuid();

        using var response = await RemoveAsync(client, other, barcode);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("not_found", await ErrorCodeAsync(response));
        Assert.Equal([barcode], await StoredBarcodesAsync(owner));
    }

    [Fact]
    public async Task RemovingFromAnUnknownProductIsNotFound()
    {
        using var client = await CreateClientAsAsync(factory, Roles.Staff);

        using var response = await RemoveAsync(client, Guid.NewGuid(), NewBarcode());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousCallersCannotChangeBarcodes()
    {
        using var staff = await CreateClientAsAsync(factory, Roles.Staff);
        var barcode = NewBarcode();
        var id = (await CreateAsync(staff, barcode)).GetProperty("id").GetGuid();
        using var client = factory.CreateClient();

        using var added = await AddAsync(client, id, NewBarcode());
        using var removed = await RemoveAsync(client, id, barcode);

        Assert.Equal(HttpStatusCode.Unauthorized, added.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, removed.StatusCode);
        Assert.Equal([barcode], await StoredBarcodesAsync(id));
    }

    private static string NewBarcode() => $"40{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}";

    private static Uri BarcodesUri(Guid id) => new($"/api/v1/products/{id}/barcodes", UriKind.Relative);

    private static async Task<JsonElement> CreateAsync(HttpClient client, string? barcode)
    {
        using var response = await client.PostAsJsonAsync(new Uri("/api/v1/products", UriKind.Relative), new { name = "Scanned", barcode }, Token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync(response);
    }

    private static Task<HttpResponseMessage> AddAsync(HttpClient client, Guid id, string barcode) =>
        client.PostAsJsonAsync(BarcodesUri(id), new { barcode }, Token);

    /// <summary>Deletes with the barcode encoded as one path segment, as encodeURIComponent would.</summary>
    private static Task<HttpResponseMessage> RemoveAsync(HttpClient client, Guid id, string barcode) =>
        client.DeleteAsync(new Uri($"/api/v1/products/{id}/barcodes/{Uri.EscapeDataString(barcode)}", UriKind.Relative), Token);

    private static List<string> Barcodes(JsonElement product) =>
        product.GetProperty("barcodes").EnumerateArray().Select(b => b.GetString()!).ToList();

    private async Task<List<string>> StoredBarcodesAsync(Guid productPublicId)
    {
        await using var db = TestDatabase.CreateContext(DatabaseUrl);
        return await db.ProductBarcodes
            .Where(b => db.Products.Any(p => p.Id == b.ProductId && p.PublicId == productPublicId))
            .OrderBy(b => b.Id)
            .Select(b => b.Barcode)
            .ToListAsync(Token);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return document.RootElement.Clone();
    }
}
