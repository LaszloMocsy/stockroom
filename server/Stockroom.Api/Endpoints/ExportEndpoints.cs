using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Stockroom.Api.Errors;
using Stockroom.Data;

namespace Stockroom.Api.Endpoints;

/// <summary><c>/api/v1/export</c>: data as files people open in spreadsheets (spec 4.6, 7.3).</summary>
internal static class ExportEndpoints
{
    public const string CsvContentType = "text/csv; charset=utf-8";

    public static IEndpointRouteBuilder MapExportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var export = endpoints.MapGroup("/export").WithTags("Export");

        export.MapGet("/products.csv", ExportProductsAsync)
            .Produces(StatusCodes.Status200OK, typeof(string), "text/csv")
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .WithName("ExportProductsCsv")
            .WithSummary("Export products as CSV")
            .WithDescription("""
                Downloads active (not archived) products, sorted by SKU, as UTF-8 CSV (RFC 4180, with a byte order mark so spreadsheet apps detect the encoding). The columns are `sku`, `name`, `barcodes`, `quantity`, and `min_stock`.

                A product's barcodes share one cell, one per line. `min_stock` is empty for products without one. Text that a spreadsheet would run as a formula (starting with `=`, `+`, `-`, or `@`) is prefixed with `'`.
                """);
        return endpoints;
    }

    private static PushStreamHttpResult ExportProductsAsync(StockroomDbContext db) =>
        TypedResults.Stream(
            async stream =>
            {
                // Rows are written as they are read, so a large catalogue is never held in memory.
                await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), bufferSize: -1, leaveOpen: true) { NewLine = "\r\n" };
                await writer.WriteLineAsync(Csv.Row("sku", "name", "barcodes", "quantity", "min_stock"));

                var products = ProductResponse.Project(
                    db.Products.Where(p => p.ArchivedAt == null).OrderBy(p => p.Sku).ThenBy(p => p.PublicId),
                    db);
                await foreach (var product in products.AsAsyncEnumerable())
                {
                    await writer.WriteLineAsync(Csv.Row(
                        Csv.Text(product.Sku),
                        Csv.Text(product.Name),
                        Csv.Text(string.Join('\n', product.Barcodes)),
                        product.Quantity.ToString(CultureInfo.InvariantCulture),
                        product.MinStock?.ToString(CultureInfo.InvariantCulture) ?? ""));
                }
            },
            CsvContentType,
            fileDownloadName: "products.csv");
}

/// <summary>Writing CSV fields and rows (RFC 4180).</summary>
internal static class Csv
{
    private static readonly char[] FormulaStarts = ['=', '+', '-', '@', '\t', '\r'];

    /// <summary>
    /// User-entered text, guarded against CSV injection: a spreadsheet runs a cell starting with one of
    /// <see cref="FormulaStarts"/> as a formula, so such text gets a leading <c>'</c>, which shows it as text.
    /// </summary>
    public static string Text(string value) =>
        value.Length > 0 && FormulaStarts.Contains(value[0]) ? "'" + value : value;

    /// <summary>One line of CSV, without the line break. Fields are quoted only when they need to be.</summary>
    public static string Row(params string[] fields) => string.Join(',', fields.Select(Field));

    private static string Field(string value) =>
        value.AsSpan().IndexOfAny(",\"\r\n") >= 0 || (value.Length > 0 && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1])))
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
}
