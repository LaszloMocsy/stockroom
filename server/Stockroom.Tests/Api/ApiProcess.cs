using System.Diagnostics;

namespace Stockroom.Tests.Api;

/// <summary>
/// Runs the built API as a separate process, the way an operator would, to observe its exit code
/// and console output. Inherited <c>STOCKROOM_*</c> variables are removed so only
/// <paramref name="settings"/> apply.
/// </summary>
internal static class ApiProcess
{
    public sealed record Result(int ExitCode, string StandardOutput, string StandardError);

    public static async Task<Result> RunAsync(IReadOnlyDictionary<string, string?> settings, CancellationToken cancellationToken)
    {
        var workingDirectory = Directory.CreateTempSubdirectory("stockroom-api-");
        try
        {
            var startInfo = new ProcessStartInfo("dotnet")
            {
                ArgumentList = { Path.Combine(AppContext.BaseDirectory, "Stockroom.Api.dll") },
                // An empty content root, so no appsettings files are picked up.
                WorkingDirectory = workingDirectory.FullName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            foreach (var key in startInfo.Environment.Keys.Where(k => k.StartsWith("STOCKROOM_", StringComparison.Ordinal)).ToList())
            {
                startInfo.Environment.Remove(key);
            }

            startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
            startInfo.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";
            foreach (var (key, value) in settings)
            {
                startInfo.Environment[key] = value;
            }

            using var process = Process.Start(startInfo)!;
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }

            return new Result(process.ExitCode, await stdout, await stderr);
        }
        finally
        {
            workingDirectory.Delete(recursive: true);
        }
    }
}
