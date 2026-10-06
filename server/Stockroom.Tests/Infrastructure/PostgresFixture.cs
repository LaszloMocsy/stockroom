using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Stockroom.Tests.Infrastructure.PostgresFixture))]

namespace Stockroom.Tests.Infrastructure;

/// <summary>
/// One real PostgreSQL server (in Docker, via Testcontainers) shared by every integration test in the run.
/// Tests take it as a constructor parameter and call <see cref="CreateDatabaseAsync"/> to get their own
/// empty database, so they stay isolated and can run in parallel.
/// </summary>
/// <remarks>
/// The container starts on first use, so a run that only includes unit tests does not need Docker.
/// It is removed when the run ends (or by the Testcontainers resource reaper if the run crashes).
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    /// <summary>Same major version as the Docker Compose example in the specification (12.1).</summary>
    public const string Image = "postgres:17";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Image).Build();
    private readonly Lazy<Task> _start;

    public PostgresFixture() => _start = new Lazy<Task>(() => _container.StartAsync());

    /// <summary>Creates a new, empty database and returns a connection string for it.</summary>
    public async Task<string> CreateDatabaseAsync(CancellationToken cancellationToken)
    {
        await _start.Value.WaitAsync(cancellationToken);

        var database = $"test_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = database }.ConnectionString;
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_start.IsValueCreated)
        {
            await _container.DisposeAsync();
        }
    }
}
