using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Stockroom.Api.Configuration;
using Stockroom.Core.Settings;
using Stockroom.Core.Users;
using Stockroom.Tests.Data;
using Stockroom.Tests.Infrastructure;
using static Stockroom.Tests.Api.TestAuth;

namespace Stockroom.Tests.Api;

/// <summary>
/// <c>GET /api/v1/export/products.csv</c>. The export covers the whole catalogue, so each test gets a database of
/// its own. The output is read back with a strict RFC 4180 parser, so malformed CSV fails the tests.
/// </summary>
public sealed class ProductExportEndpointTests(PostgresFixture postgres)
{
    private static readonly Uri ExportUri = new("/api/v1/export/products.csv", UriKind.Relative);

    private static readonly string[] Header = ["sku", "name", "barcodes", "quantity", "min_stock"];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task StaffCanDownloadActiveProductsSortedBySku()
    {
        await using var app = await StockroomApiFactory.CreateAsync(postgres);
        using var client = await CreateClientAsAsync(app, Roles.Staff);
        await CreateAsync(client, sku: "B-2", name: "Washer", barcodes: [], quantity: 0, minStock: null);
        await CreateAsync(client, sku: "A-1", name: "Hex bolt", barcodes: ["4006381333931", "QR-42"], quantity: 12, minStock: 5);
        var archived = TestProducts.New("A-0");
        archived.ArchivedAt = DateTimeOffset.UtcNow;
        await TestDatabase.AddAsync(app.Settings[StockroomOptions.DatabaseUrlKey]!, archived);

        using var response = await client.GetAsync(ExportUri, Token);
        var bytes = await response.Content.ReadAsByteArrayAsync(Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("products.csv", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.Equal(
            [
                Header,
                ["A-1", "Hex bolt", "4006381333931\nQR-42", "12", "5"],
                ["B-2", "Washer", "", "0", ""],
            ],
            ParseCsv(Encoding.UTF8.GetString(bytes[3..])));
    }

    [Fact]
    public async Task SpecialCharactersAreEscapedAndSurviveARoundTrip()
    {
        await using var app = await StockroomApiFactory.CreateAsync(postgres);
        using var client = await CreateClientAsAsync(app, Roles.Staff);
        string[] names = ["Bolts, M8", "The \"good\" ones", "Two\r\nlines", " padded ", "Csavar – horganyzott, ő", "plain"];
        for (var i = 0; i < names.Length; i++)
        {
            await CreateAsync(client, sku: $"S-{i}", name: names[i], barcodes: [$"https://example.com/p/{i}?a=1,b=\"2\""], quantity: i, minStock: null);
        }

        var rows = await DownloadAsync(client);

        Assert.Equal(names, rows.Skip(1).Select(r => r[1]));
        Assert.Equal(names.Select((_, i) => $"https://example.com/p/{i}?a=1,b=\"2\""), rows.Skip(1).Select(r => r[2]));
    }

    [Fact]
    public async Task TextThatASpreadsheetWouldRunIsDefused()
    {
        await using var app = await StockroomApiFactory.CreateAsync(postgres);
        using var client = await CreateClientAsAsync(app, Roles.Staff);
        await CreateAsync(client, sku: "=SUM(1)", name: "=HYPERLINK(\"http://evil.example\",\"x\")", barcodes: ["@cmd", "+1"], quantity: 3, minStock: 1);
        await CreateAsync(client, sku: "Z-1", name: "-10% lot", barcodes: [], quantity: 0, minStock: null);

        var rows = await DownloadAsync(client);

        Assert.Equal(["'=SUM(1)", "'=HYPERLINK(\"http://evil.example\",\"x\")", "'@cmd\n+1", "3", "1"], rows[1]);
        Assert.Equal("'-10% lot", rows[2][1]);
    }

    [Fact]
    public async Task NegativeQuantitiesAreWrittenAsNumbers()
    {
        await using var app = await StockroomApiFactory.CreateAsync(postgres);
        using var client = await CreateClientAsAsync(app, Roles.Admin);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISettingsStore>().SetAsync(StockroomSettings.AllowNegativeStock, true, Token);
        }

        var id = await CreateAsync(client, sku: "N-1", name: "Oversold", barcodes: [], quantity: 1, minStock: null);
        using var issued = await client.PostAsJsonAsync(new Uri("/api/v1/stock/movements", UriKind.Relative), new { product_id = id, type = "issue", quantity = 3 }, Token);
        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);

        var rows = await DownloadAsync(client);

        Assert.Equal("-2", rows[1][3]);
    }

    [Fact]
    public async Task AnEmptyCatalogueHasOnlyTheHeader()
    {
        await using var app = await StockroomApiFactory.CreateAsync(postgres);
        using var client = await CreateClientAsAsync(app, Roles.Staff);

        Assert.Equal([Header], await DownloadAsync(client));
    }

    [Fact]
    public async Task AnonymousCallersCannotExport()
    {
        await using var app = await StockroomApiFactory.CreateAsync(postgres);
        using var client = app.CreateClient();

        using var response = await client.GetAsync(ExportUri, Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<Guid> CreateAsync(HttpClient client, string sku, string name, string[] barcodes, int quantity, int? minStock)
    {
        using var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/products", UriKind.Relative),
            new { sku, name, min_stock = minStock, initial_quantity = quantity },
            Token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        var id = document.RootElement.GetProperty("id").GetGuid();
        foreach (var barcode in barcodes)
        {
            using var added = await client.PostAsJsonAsync(new Uri($"/api/v1/products/{id}/barcodes", UriKind.Relative), new { barcode }, Token);
            Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        }

        return id;
    }

    private static async Task<List<string[]>> DownloadAsync(HttpClient client)
    {
        using var response = await client.GetAsync(ExportUri, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync(Token));
        return ParseCsv(text.TrimStart('﻿'));
    }

    /// <summary>
    /// Parses RFC 4180 CSV strictly: CRLF ends every record, a quote may appear only around a whole field or doubled
    /// inside one, and every record has as many fields as the header. Anything else fails the test.
    /// </summary>
    private static List<string[]> ParseCsv(string text)
    {
        var records = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '"')
            {
                i++;
                while (true)
                {
                    Assert.True(i < text.Length, "Unterminated quoted field.");
                    if (text[i] == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            field.Append('"');
                            i += 2;
                            continue;
                        }

                        i++;
                        break;
                    }

                    field.Append(text[i++]);
                }
            }
            else
            {
                while (i < text.Length && text[i] is not (',' or '\r' or '\n'))
                {
                    Assert.NotEqual('"', text[i]);
                    field.Append(text[i++]);
                }
            }

            fields.Add(field.ToString());
            field.Clear();
            Assert.True(i < text.Length, "The last record must end with CRLF.");
            if (text[i] == ',')
            {
                i++;
                continue;
            }

            Assert.True(text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n', $"Expected CRLF at offset {i}.");
            i += 2;
            records.Add([.. fields]);
            fields.Clear();
        }

        Assert.All(records, r => Assert.Equal(Header.Length, r.Length));
        return records;
    }
}
