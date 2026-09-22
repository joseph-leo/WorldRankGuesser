using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SportsRankingService.Services;

namespace SportsRankingService.Tests.Services;

/// <summary>
/// Runs the machine's real curl against a loopback socket (no external network), so the same tests prove the
/// process handling on Windows, macOS and Linux. They need curl on the PATH, as the fetcher does.
/// </summary>
public class CurlFetcherTests
{
    /// <summary>Accepts one connection, captures the request text and answers with <paramref name="response"/> (null: never answers).</summary>
    private sealed class LoopbackServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

        public LoopbackServer(string? response)
        {
            _listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/rankings?page=1&size=100";
            Request = Task.Run(async () =>
            {
                using TcpClient client = await _listener.AcceptTcpClientAsync();
                var buffer = new byte[8192];
                int read = await client.GetStream().ReadAsync(buffer);
                string request = Encoding.ASCII.GetString(buffer, 0, read);

                if (response is null)
                {
                    await Task.Delay(Timeout.Infinite, _stopped.Token).ContinueWith(_ => { });
                    return request;
                }

                await client.GetStream().WriteAsync(Encoding.UTF8.GetBytes(response));
                return request;
            });
        }

        private readonly CancellationTokenSource _stopped = new();

        public string Url { get; }

        public Task<string> Request { get; }

        public static string Http(string status, string body) =>
            $"HTTP/1.1 {status}\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";

        public void Dispose()
        {
            _stopped.Cancel();
            _listener.Stop();
        }
    }

    private static CurlFetcher Fetcher() => new(NullLogger<CurlFetcher>.Instance);

    [Fact]
    public async Task Returns_the_body_decoded_as_utf8()
    {
        using var server = new LoopbackServer(LoopbackServer.Http("200 OK", """{"name":"Türkiye"}"""));

        string? body = await Fetcher().GetStringAsync(server.Url, CancellationToken.None);

        Assert.Equal("""{"name":"Türkiye"}""", body);
    }

    [Fact]
    public async Task Requests_the_url_unchanged_and_identifies_itself()
    {
        using var server = new LoopbackServer(LoopbackServer.Http("200 OK", "{}"));

        await Fetcher().GetStringAsync(server.Url, CancellationToken.None);

        string request = await server.Request;
        Assert.StartsWith("GET /rankings?page=1&size=100 HTTP/1.1", request);
        Assert.Contains($"User-Agent: {CurlFetcher.UserAgent}\r\n", request);
    }

    [Fact]
    public async Task Non_success_status_yields_null()
    {
        using var server = new LoopbackServer(LoopbackServer.Http("403 Forbidden", "blocked"));

        string? body = await Fetcher().GetStringAsync(server.Url, CancellationToken.None);

        Assert.Null(body);
    }

    [Fact]
    public async Task Missing_curl_binary_yields_null()
    {
        var fetcher = new CurlFetcher("curl-is-not-installed-here", NullLogger<CurlFetcher>.Instance);

        string? body = await fetcher.GetStringAsync("http://127.0.0.1:9/", CancellationToken.None);

        Assert.Null(body);
    }

    [Fact]
    public async Task Cancellation_stops_curl_and_throws()
    {
        using var server = new LoopbackServer(response: null);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var elapsed = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Fetcher().GetStringAsync(server.Url, cancel.Token));

        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10), $"took {elapsed.Elapsed}");
    }
}
