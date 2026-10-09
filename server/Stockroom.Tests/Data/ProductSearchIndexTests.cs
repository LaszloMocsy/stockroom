using Npgsql;
using Stockroom.Tests.Infrastructure;

namespace Stockroom.Tests.Data;

/// <summary>The trigram indexes behind product search (spec 4.1) serve case-insensitive substring matches.</summary>
public sealed class ProductSearchIndexTests(PostgresFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("products", "name", "ix_products_name_trgm")]
    [InlineData("products", "sku", "ix_products_sku_trgm")]
    [InlineData("product_barcodes", "barcode", "ix_product_barcodes_barcode_trgm")]
    public async Task ASubstringSearchCanUseTheTrigramIndex(string table, string column, string index)
    {
        var databaseUrl = await TestDatabase.CreateMigratedAsync(postgres, Token);
        await using var connection = new NpgsqlConnection(databaseUrl);
        await connection.OpenAsync(Token);
        await using var transaction = await connection.BeginTransactionAsync(Token);

        // An empty table is cheapest to scan, so rule that out to see whether the index applies at all.
        await using var command = new NpgsqlCommand(
            $"SET LOCAL enable_seqscan = off; EXPLAIN SELECT 1 FROM {table} WHERE {column} ILIKE '%bolt%'",
            connection,
            transaction);
        var plan = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync(Token))
        {
            while (await reader.ReadAsync(Token))
            {
                plan.Add(reader.GetString(0));
            }
        }

        Assert.Contains(plan, line => line.Contains(index, StringComparison.Ordinal));
    }
}
