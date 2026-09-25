using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using SportsRankingService.Configuration;
using SportsRankingService.Services;

namespace SportsRankingService.Tests.Services;

/// <summary>
/// At the end of a run that stored a new release, one POST tells the game to re-read the view. Nothing configured,
/// or nothing new: no request. A failure is a warning and never an exception, because the game's own timer covers it.
/// </summary>
public class RefreshNotifierTests
{
    private const string Url = "https://ca-wrg-staging-game.example.azurecontainerapps.io/api/rankings/refresh";

    private sealed class RecordingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

        public Exception? Throws { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request, request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            if (Throws is not null)
            {
                throw Throws;
            }

            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(RefreshNotifier.ClientName, name);
            return new HttpClient(handler, disposeHandler: false);
        }
    }

    private static (RefreshNotifier Notifier, RecordingHandler Handler, FakeLogger<RefreshNotifier> Log) Build(
        NotifyOptions options, HttpStatusCode status = HttpStatusCode.OK, string body = "{\"rows\":3587,\"drawableCountries\":218,\"loadedAt\":\"2026-09-25T00:59:53Z\"}", Exception? throws = null)
    {
        var handler = new RecordingHandler(status, body) { Throws = throws };
        var log = new FakeLogger<RefreshNotifier>();
        return (new RefreshNotifier(new Factory(handler), options, log), handler, log);
    }

    private static readonly UpdateSummary Inserted = new(Feeds: 54, Inserted: 5, Unchanged: 49, Failed: 0);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Without_a_url_it_sends_nothing_and_says_so_once(string? url)
    {
        var (notifier, handler, log) = Build(new NotifyOptions { Url = url, Token = "t0k" });

        await notifier.NotifyAsync(Inserted, CancellationToken.None);

        Assert.Empty(handler.Requests);
        Assert.Single(log.Collector.GetSnapshot(), r => r.Level == LogLevel.Information && r.Message.Contains("Notify:Url"));
    }

    [Fact]
    public async Task A_run_with_nothing_new_sends_nothing()
    {
        var (notifier, handler, _) = Build(new NotifyOptions { Url = Url, Token = "t0k" });

        await notifier.NotifyAsync(new UpdateSummary(54, Inserted: 0, Unchanged: 54, Failed: 0), CancellationToken.None);

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_run_with_a_new_release_posts_once_with_the_token_and_logs_the_counts()
    {
        var (notifier, handler, log) = Build(new NotifyOptions { Url = Url, Token = "t0k" });

        await notifier.NotifyAsync(Inserted, CancellationToken.None);

        var (request, _) = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(Url, request.RequestUri!.ToString());
        Assert.Equal(["t0k"], request.Headers.GetValues(RefreshNotifier.TokenHeader));
        FakeLogRecord line = Assert.Single(log.Collector.GetSnapshot(), r => r.Level == LogLevel.Information);
        Assert.Contains("3587", line.Message);
        Assert.Contains("218", line.Message);
    }

    /// <summary>Some feeds failed but others stored a new release: the view changed, so the game is told.</summary>
    [Fact]
    public async Task A_run_with_failures_and_a_new_release_still_posts()
    {
        var (notifier, handler, _) = Build(new NotifyOptions { Url = Url, Token = "t0k" });

        await notifier.NotifyAsync(new UpdateSummary(54, Inserted: 1, Unchanged: 48, Failed: 5), CancellationToken.None);

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_non_success_status_warns_with_the_status_and_does_not_throw()
    {
        var (notifier, _, log) = Build(new NotifyOptions { Url = Url, Token = "t0k" }, HttpStatusCode.Unauthorized, "");

        await notifier.NotifyAsync(Inserted, CancellationToken.None);

        FakeLogRecord warning = Assert.Single(log.Collector.GetSnapshot(), r => r.Level == LogLevel.Warning);
        Assert.Contains("401", warning.Message);
    }

    [Fact]
    public async Task A_transport_failure_warns_and_does_not_throw()
    {
        var (notifier, _, log) = Build(new NotifyOptions { Url = Url, Token = "t0k" }, throws: new TaskCanceledException("timed out"));

        await notifier.NotifyAsync(Inserted, CancellationToken.None);

        Assert.Single(log.Collector.GetSnapshot(), r => r.Level == LogLevel.Warning);
    }
}
