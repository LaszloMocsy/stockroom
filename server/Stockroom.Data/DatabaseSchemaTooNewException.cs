namespace Stockroom.Data;

/// <summary>
/// The database has migrations that this build does not know, so it was created or upgraded by a
/// newer version of Stockroom. Running against it could misread or corrupt data.
/// </summary>
public sealed class DatabaseSchemaTooNewException(IReadOnlyList<string> unknownMigrations)
    : Exception($"The database schema is newer than this server. Unknown migrations: {string.Join(", ", unknownMigrations)}.")
{
    public IReadOnlyList<string> UnknownMigrations { get; } = unknownMigrations;
}
