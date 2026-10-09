using System.Reflection;
using Stockroom.Tests.Infrastructure;

namespace Stockroom.Tests.Api;

/// <summary>
/// Keeps <c>server/openapi/openapi.v1.json</c>, the committed API contract that the TypeScript client is
/// generated from (spec 9.1, D8), equal to the document the API serves. Any API change has to update the
/// snapshot in the same commit, which makes contract changes visible in review.
/// </summary>
public sealed class OpenApiSnapshotTests(StockroomApiFactory factory) : IClassFixture<StockroomApiFactory>
{
    /// <summary>Set to <c>1</c> to write the served document to the snapshot instead of comparing.</summary>
    public const string UpdateVariable = "UPDATE_OPENAPI_SNAPSHOT";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CommittedSnapshotMatchesTheServedDocument()
    {
        using var client = factory.CreateClient();
        var served = Normalise(await client.GetStringAsync(new Uri("/api/v1/openapi.json", UriKind.Relative), Token));
        var path = SnapshotPath();

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, served, Token);
            return;
        }

        Assert.True(File.Exists(path), $"The OpenAPI snapshot {path} is missing. {UpdateHint}");
        var committed = Normalise(await File.ReadAllTextAsync(path, Token));

        Assert.True(committed == served, $"The OpenAPI snapshot {path} is stale: the API changed. {UpdateHint}");
    }

    private const string UpdateHint =
        $"If the change is intended, regenerate it with `{UpdateVariable}=1 dotnet test --project Stockroom.Tests -- --filter-class '*{nameof(OpenApiSnapshotTests)}'` " +
        "from server/, and commit it with the change.";

    /// <summary>LF line endings and one trailing newline, as Git stores the file (see .gitattributes).</summary>
    private static string Normalise(string json) => json.ReplaceLineEndings("\n").TrimEnd('\n') + "\n";

    private static string SnapshotPath() =>
        typeof(OpenApiSnapshotTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "OpenApiSnapshotPath").Value!;
}
