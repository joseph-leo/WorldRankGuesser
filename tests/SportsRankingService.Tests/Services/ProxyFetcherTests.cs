using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using SportsRankingService.Configuration;
using SportsRankingService.Services;

namespace SportsRankingService.Tests.Services;

/// <summary>
/// The fetcher for a feed whose site refuses hosting addresses: every request goes to the Worker in proxy/ as
/// GET &lt;Proxy:Url&gt;/fetch?url=&lt;target&gt; with the shared token, and the Worker's answer is the upstream's. Without a
/// configured URL (a run from the owner's machine) it fetches directly through the HTTP fetcher and says so once.
/// </summary>
public class ProxyFetcherTests
{
    private const string Target = "https://www.wbsc.org/api/v1/rankings/sport/show?sportId=baseball-m&date=2026-09-15&fullView=1&preview=&lang=en";

    private sealed class RecordingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public Exception? Throws { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (Throws is not null)
            {
                throw Throws;
            }

            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(ProxyFetcher.ClientName, name);
            return new HttpClient(handler, disposeHandler: false);
        }
    }

    private sealed class DirectFetcher(string? body) : IHttpFetcher
    {
        public string Name => HttpFetcher.FetcherName;

        public List<string> Requested { get; } = [];

        public Task<string?> GetStringAsync(string url, CancellationToken cancellationToken)
        {
            Requested.Add(url);
            return Task.FromResult(body);
        }
    }

    private static (ProxyFetcher Fetcher, RecordingHandler Handler, DirectFetcher Direct, FakeLogger<ProxyFetcher> Log) Build(
        ProxyOptions options, HttpStatusCode status = HttpStatusCode.OK, string body = "{\"rankings\":[]}", Exception? throws = null)
    {
        var handler = new RecordingHandler(status, body) { Throws = throws };
        var direct = new DirectFetcher("direct body");
        var log = new FakeLogger<ProxyFetcher>();
        return (new ProxyFetcher(new Factory(handler), direct, options, log), handler, direct, log);
    }

    [Fact]
    public async Task Sends_the_target_encoded_in_the_query_with_the_token_header()
    {
        var (fetcher, handler, direct, _) = Build(new ProxyOptions { Url = "https://wrg-proxy.example.workers.dev/", Token = "t0k" });

        string? body = await fetcher.GetStringAsync(Target, CancellationToken.None);

        Assert.Equal("{\"rankings\":[]}", body);
        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://wrg-proxy.example.workers.dev/fetch?url=" + Uri.EscapeDataString(Target), request.RequestUri!.ToString());
        Assert.Equal(["t0k"], request.Headers.GetValues(ProxyFetcher.TokenHeader));
        Assert.Empty(direct.Requested);
    }

    [Fact]
    public async Task A_non_success_status_logs_it_and_returns_null()
    {
        var (fetcher, _, _, log) = Build(new ProxyOptions { Url = "https://wrg-proxy.example.workers.dev", Token = "t0k" }, HttpStatusCode.Forbidden, "Request blocked");

        string? body = await fetcher.GetStringAsync(Target, CancellationToken.None);

        Assert.Null(body);
        FakeLogRecord warning = Assert.Single(log.Collector.GetSnapshot(), r => r.Level == LogLevel.Warning);
        Assert.Contains("403", warning.Message);
        Assert.Contains(Target, warning.Message);
    }

    [Fact]
    public async Task A_transport_failure_returns_null()
    {
        var (fetcher, _, _, log) = Build(new ProxyOptions { Url = "https://wrg-proxy.example.workers.dev", Token = "t0k" }, throws: new HttpRequestException("connection refused"));

        string? body = await fetcher.GetStringAsync(Target, CancellationToken.None);

        Assert.Null(body);
        Assert.Contains(log.Collector.GetSnapshot(), r => r.Level == LogLevel.Error && r.Message.Contains(Target));
    }

    /// <summary>The compose stack passes an empty string when the shell variable is unset, so empty means unconfigured too.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Without_a_url_it_fetches_directly_and_warns_once(string? url)
    {
        var (fetcher, handler, direct, log) = Build(new ProxyOptions { Url = url, Token = "t0k" });

        string? first = await fetcher.GetStringAsync(Target, CancellationToken.None);
        string? second = await fetcher.GetStringAsync("https://www.wbsc.org/en/rankings", CancellationToken.None);

        Assert.Equal("direct body", first);
        Assert.Equal("direct body", second);
        Assert.Equal([Target, "https://www.wbsc.org/en/rankings"], direct.Requested);
        Assert.Empty(handler.Requests);
        Assert.Single(log.Collector.GetSnapshot(), r => r.Level == LogLevel.Warning);
    }

    /// <summary>A rotation slip: the URL is set but the token is not. The request still goes out (the Worker answers 401), and one warning names the cause.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task With_a_url_but_no_token_it_warns_once_and_still_asks_the_proxy(string? token)
    {
        var (fetcher, handler, _, log) = Build(new ProxyOptions { Url = "https://wrg-proxy.example.workers.dev", Token = token }, HttpStatusCode.Unauthorized, "missing or wrong X-Proxy-Token");

        string? first = await fetcher.GetStringAsync(Target, CancellationToken.None);
        string? second = await fetcher.GetStringAsync("https://www.wbsc.org/en/rankings", CancellationToken.None);

        Assert.Null(first);
        Assert.Null(second);
        Assert.Equal(2, handler.Requests.Count);
        FakeLogRecord warning = Assert.Single(log.Collector.GetSnapshot(), r => r.Level == LogLevel.Warning && r.Message.Contains("Proxy:Token"));
        Assert.DoesNotContain(Target, warning.Message);
    }

    [Fact]
    public void Its_name_is_Proxy()
    {
        var (fetcher, _, _, _) = Build(new ProxyOptions());

        Assert.Equal("Proxy", fetcher.Name);
        Assert.Equal(ProxyFetcher.FetcherName, fetcher.Name);
    }
}
