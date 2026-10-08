using Microsoft.EntityFrameworkCore;
using Stockroom.Data.Auth;
using Stockroom.Tests.Infrastructure;

namespace Stockroom.Tests.Data;

public sealed class SigningKeyStoreTests(PostgresFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FirstCallCreatesAKeyThatLaterCallsReturn()
    {
        var databaseUrl = await TestDatabase.CreateMigratedAsync(postgres, Token);

        var first = await GetOrCreateAsync(databaseUrl);
        var second = await GetOrCreateAsync(databaseUrl);

        Assert.Equal(SigningKeyStore.KeySizeInBytes, first.Length);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task ConcurrentFirstCallsCreateOneKey()
    {
        var databaseUrl = await TestDatabase.CreateMigratedAsync(postgres, Token);

        var keys = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => GetOrCreateAsync(databaseUrl)));

        Assert.All(keys, key => Assert.Equal(keys[0], key));
        await using var db = TestDatabase.CreateContext(databaseUrl);
        Assert.Equal(1, await db.SigningKeys.CountAsync(Token));
    }

    private static async Task<byte[]> GetOrCreateAsync(string databaseUrl)
    {
        await using var db = TestDatabase.CreateContext(databaseUrl);
        return await new SigningKeyStore(db, TestDatabase.Ids, TimeProvider.System).GetOrCreateAsync(Token);
    }
}
