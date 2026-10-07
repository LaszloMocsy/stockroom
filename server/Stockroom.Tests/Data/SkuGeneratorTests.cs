using Microsoft.Extensions.DependencyInjection;
using Stockroom.Core.Products;
using Stockroom.Data.Products;
using Stockroom.Tests.Api;
using Stockroom.Tests.Infrastructure;

namespace Stockroom.Tests.Data;

public sealed class SkuGeneratorTests(PostgresFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SkusCountUpFromOne()
    {
        var databaseUrl = await TestDatabase.CreateMigratedAsync(postgres, Token);
        await using var db = TestDatabase.CreateContext(databaseUrl);
        var generator = new SkuGenerator(db);

        Assert.Equal("SR-000001", await generator.NextAsync(Token));
        Assert.Equal("SR-000002", await generator.NextAsync(Token));
        Assert.Equal("SR-000003", await generator.NextAsync(Token));
    }

    [Fact]
    public async Task ConcurrentGenerationYieldsUniqueValues()
    {
        const int Workers = 16;
        const int SkusPerWorker = 50;
        var databaseUrl = await TestDatabase.CreateMigratedAsync(postgres, Token);

        // Each worker has its own context and connection, as concurrent requests would.
        var batches = await Task.WhenAll(Enumerable.Range(0, Workers).Select(_ => Task.Run(async () =>
        {
            await using var db = TestDatabase.CreateContext(databaseUrl);
            var generator = new SkuGenerator(db);
            var skus = new List<string>(SkusPerWorker);
            for (var i = 0; i < SkusPerWorker; i++)
            {
                skus.Add(await generator.NextAsync(Token));
            }

            return skus;
        }, Token)));

        var all = batches.SelectMany(b => b).ToList();
        Assert.Equal(Workers * SkusPerWorker, all.Distinct().Count());
        Assert.Equal(
            Enumerable.Range(1, Workers * SkusPerWorker).Select(n => Sku.FromNumber(n)).Order(StringComparer.Ordinal),
            all.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task TheApiProvidesTheGenerator()
    {
        await using var factory = await StockroomApiFactory.CreateAsync(postgres);
        await using var scope = factory.Services.CreateAsyncScope();

        var generator = scope.ServiceProvider.GetRequiredService<ISkuGenerator>();

        Assert.IsType<SkuGenerator>(generator);
        Assert.Equal("SR-000001", await generator.NextAsync(Token));
    }
}
