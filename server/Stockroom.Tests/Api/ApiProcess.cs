using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Stockroom.Tests.Api;

/// <summary>
/// Runs the built API as a separate process, the way an operator would, to observe its exit code
/// and console output. Inherited <c>STOCKROOM_*</c> variables are removed so only the given
/// settings apply.
/// </summary>
public static partial class ApiProcess
{
    public sealed record Result(int ExitCode, string StandardOutput, string StandardError);

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Starts the API and waits for it to exit (used for startup failures and commands such as <c>seed</c>).
    /// </summary>
    public static async Task<Result> RunAsync(
        IReadOnlyDictionary<string, string?> settings,
        CancellationToken cancellationToken,
        string environment = "Production",
        params IReadOnlyList<string> args)
    {
        using var workingDirectory = new TempDirectory();
        using var process = Start(settings, workingDirectory.Path, environment, args);
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
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

    /// <summary>Starts the API on a free port and waits until it is listening.</summary>
    public static async Task<RunningApi> StartAsync(IReadOnlyDictionary<string, string?> settings, CancellationToken cancellationToken)
    {
        var workingDirectory = new TempDirectory();
        var process = Start(settings, workingDirectory.Path, "Production", []);
        var api = new RunningApi(process, workingDirectory);
        try
        {
            await api.WaitUntilListeningAsync(cancellationToken);
            return api;
        }
        catch
        {
            await api.DisposeAsync();
            throw;
        }
    }

    private static Process Start(IReadOnlyDictionary<string, string?> settings, string workingDirectory, string environment, IReadOnlyList<string> args)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            ArgumentList = { Path.Combine(AppContext.BaseDirectory, "Stockroom.Api.dll") },
            // An empty content root, so no appsettings files are picked up.
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        foreach (var key in startInfo.Environment.Keys.Where(k => k.StartsWith("STOCKROOM_", StringComparison.Ordinal)).ToList())
        {
            startInfo.Environment.Remove(key);
        }

        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = environment;
        startInfo.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";
        foreach (var (key, value) in settings)
        {
            startInfo.Environment[key] = value;
        }

        return Process.Start(startInfo)!;
    }

    /// <summary>A running API process whose JSON log lines can be inspected.</summary>
    public sealed partial class RunningApi : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly TempDirectory _workingDirectory;
        private readonly List<JsonDocument> _lines = [];
        private readonly Task _reader;
        private readonly Task _stderrDrain;

        public RunningApi(Process process, TempDirectory workingDirectory)
        {
            _process = process;
            _workingDirectory = workingDirectory;
            _reader = Task.Run(ReadLinesAsync);
            // Drained so a chatty stderr cannot fill the pipe and block the process.
            _stderrDrain = process.StandardError.ReadToEndAsync();
        }

        public Uri BaseAddress { get; private set; } = null!;

        /// <summary>Every JSON object the process has written to stdout so far.</summary>
        public IReadOnlyList<JsonElement> Lines
        {
            get
            {
                lock (_lines)
                {
                    return [.. _lines.Select(d => d.RootElement)];
                }
            }
        }

        /// <summary>Waits for a log line matching <paramref name="predicate"/> and returns it.</summary>
        public async Task<JsonElement> WaitForLineAsync(Func<JsonElement, bool> predicate, CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);
            try
            {
                while (true)
                {
                    foreach (var line in Lines)
                    {
                        if (predicate(line))
                        {
                            return line;
                        }
                    }

                    await Task.Delay(25, timeout.Token);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"No matching log line within {Timeout.TotalSeconds:0} s. Output so far:{Environment.NewLine}{string.Join(Environment.NewLine, Lines)}");
            }
        }

        public async Task WaitUntilListeningAsync(CancellationToken cancellationToken)
        {
            var line = await WaitForLineAsync(l => ListeningUrl(l) is not null, cancellationToken);
            BaseAddress = new Uri(ListeningUrl(line)!);
        }

        public async ValueTask DisposeAsync()
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }

            await _process.WaitForExitAsync();
            await _reader;
            await _stderrDrain;
            _process.Dispose();
            lock (_lines)
            {
                foreach (var line in _lines)
                {
                    line.Dispose();
                }
            }

            _workingDirectory.Dispose();
        }

        private static string? ListeningUrl(JsonElement line)
        {
            if (!line.TryGetProperty("Message", out var message) || message.GetString() is not { } text)
            {
                return null;
            }

            var match = ListeningPattern().Match(text);
            return match.Success ? match.Groups["url"].Value : null;
        }

        private async Task ReadLinesAsync()
        {
            while (await _process.StandardOutput.ReadLineAsync() is { } text)
            {
                if (!text.StartsWith('{'))
                {
                    continue;
                }

                JsonDocument document;
                try
                {
                    document = JsonDocument.Parse(text);
                }
                catch (JsonException)
                {
                    // Not a log line after all; keep reading so later lines are still seen.
                    continue;
                }

                lock (_lines)
                {
                    _lines.Add(document);
                }
            }
        }

        [GeneratedRegex(@"^Now listening on: (?<url>http://\S+)$")]
        private static partial Regex ListeningPattern();
    }

    public sealed class TempDirectory : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("stockroom-api-");

        public string Path => _directory.FullName;

        public void Dispose() => _directory.Delete(recursive: true);
    }
}
