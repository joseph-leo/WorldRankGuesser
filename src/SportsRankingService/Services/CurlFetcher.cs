using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace SportsRankingService.Services;

/// <summary>
/// Fetches through the machine's curl, for a feed that refuses .NET's TLS handshake yet answers curl (BWF, 2026-09-18).
/// curl is started directly, never through a shell, and its arguments go through <see cref="ProcessStartInfo.ArgumentList"/>,
/// so there is no quoting, no NUL versus /dev/null and no PowerShell "curl" alias to differ between Windows, macOS and Linux;
/// the body comes back on stdout. It needs curl on the PATH: Windows 10+, macOS and most Linux distributions ship it.
/// </summary>
public sealed class CurlFetcher : IHttpFetcher
{
    public const string FetcherName = "Curl";

    /// <summary>Says what the client is and where to find its owner, rather than imitating a browser.</summary>
    public const string UserAgent = "SportsRankingService/1.0 (+https://github.com/joseph-leo/SportsRankingService)";

    private const int TimeoutSeconds = 60;

    private readonly string _executable;
    private readonly ILogger<CurlFetcher> _logger;

    public CurlFetcher(ILogger<CurlFetcher> logger) : this("curl", logger)
    {
    }

    internal CurlFetcher(string executable, ILogger<CurlFetcher> logger)
    {
        _executable = executable;
        _logger = logger;
    }

    public string Name => FetcherName;

    public async Task<string?> GetStringAsync(string url, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching {Url} with curl", url);

        using Process process = new();
        process.StartInfo = StartInfo(url);

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            // Thrown on every OS when the executable cannot be started.
            _logger.LogError(ex, "Fetch failed. Could not start '{Executable}'; the Curl fetcher needs curl on the PATH. {Url}", _executable, url);
            return null;
        }

        try
        {
            // Both pipes are drained while curl runs; reading one after the other can deadlock on a full pipe buffer.
            Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode == 0)
            {
                return await output;
            }

            _logger.LogWarning("Fetch failed. curl exited with {ExitCode} for {Url}: {Error}", process.ExitCode, url, (await error).Trim());
            return null;
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            throw;
        }
    }

    private ProcessStartInfo StartInfo(string url)
    {
        ProcessStartInfo startInfo = new(_executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // Without this Windows decodes the pipe with the console code page and mangles non-ASCII names.
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        string[] arguments =
        [
            "--disable",        // must come first: ignore the user's .curlrc / _curlrc
            "--silent",
            "--show-error",     // still explain a failure on stderr
            "--fail",           // HTTP 400+ becomes exit code 22 with no body
            "--location",
            "--max-time", TimeoutSeconds.ToString(),
            "--user-agent", UserAgent,
            "--url", url,
        ];

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // curl had already exited.
        }
    }
}
