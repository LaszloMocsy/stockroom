using Npgsql;

namespace Stockroom.Tests.Infrastructure;

public sealed class PostgresFixtureTests(PostgresFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ConnectsToARealPostgreSqlServer()
    {
        var connectionString = await postgres.CreateDatabaseAsync(Token);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Token);

        await using var command = new NpgsqlCommand("SELECT current_setting('server_version_num')::int, current_database()", connection);
        await using var reader = await command.ExecuteReaderAsync(Token);
        await reader.ReadAsync(Token);

        Assert.Equal(17, reader.GetInt32(0) / 10_000);
        Assert.Equal(new NpgsqlConnectionStringBuilder(connectionString).Database, reader.GetString(1));
    }

    [Fact]
    public async Task EachCallGetsItsOwnEmptyDatabase()
    {
        var first = await postgres.CreateDatabaseAsync(Token);
        var second = await postgres.CreateDatabaseAsync(Token);

        await using (var connection = new NpgsqlConnection(first))
        {
            await connection.OpenAsync(Token);
            await using var create = new NpgsqlCommand("CREATE TABLE marker (id int)", connection);
            await create.ExecuteNonQueryAsync(Token);
        }

        await using var other = new NpgsqlConnection(second);
        await other.OpenAsync(Token);
        await using var query = new NpgsqlCommand("SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public'", other);

        Assert.NotEqual(new NpgsqlConnectionStringBuilder(first).Database, new NpgsqlConnectionStringBuilder(second).Database);
        Assert.Equal(0L, (long)(await query.ExecuteScalarAsync(Token))!);
    }
}
