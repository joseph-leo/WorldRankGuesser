using SportsRankingService.Services;

namespace SportsRankingService.Tests.Services;

/// <summary>
/// Several feeds read the same page (one FIG page holds every apparatus table, one WBSC page every
/// sport's release dates), and the feeds run in parallel, so the second request usually arrives
/// while the first is still in flight. The gated fake holds every request open until released.
/// </summary>
public class CachingFetcherTests
{
    private sealed class GatedFetcher(string? body) : IHttpFetcher
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Name => "Gated";

        public List<string> Requested { get; } = [];

        public void Release() => _gate.SetResult();

        public async Task<string?> GetStringAsync(string url, CancellationToken cancellationToken)
        {
            lock (Requested)
            {
                Requested.Add(url);
            }

            await _gate.Task;
            return body;
        }
    }

    [Fact]
    public async Task Concurrent_requests_for_one_url_reach_the_inner_fetcher_once()
    {
        var inner = new GatedFetcher("page");
        var fetcher = new CachingFetcher(inner);

        Task<string?> first = fetcher.GetStringAsync("http://fig/rg", CancellationToken.None);
        Task<string?> second = fetcher.GetStringAsync("http://fig/rg", CancellationToken.None);
        inner.Release();

        Assert.Equal("page", await first);
        Assert.Equal("page", await second);
        Assert.Equal(["http://fig/rg"], inner.Requested);
    }

    [Fact]
    public async Task A_later_request_for_the_same_url_is_answered_from_the_first()
    {
        var inner = new GatedFetcher("page");
        var fetcher = new CachingFetcher(inner);
        inner.Release();

        await fetcher.GetStringAsync("http://fig/rg", CancellationToken.None);
        string? again = await fetcher.GetStringAsync("http://fig/rg", CancellationToken.None);

        Assert.Equal("page", again);
        Assert.Single(inner.Requested);
    }

    [Fact]
    public async Task Different_urls_are_fetched_separately()
    {
        var inner = new GatedFetcher("page");
        var fetcher = new CachingFetcher(inner);
        inner.Release();

        await fetcher.GetStringAsync("http://fig/rg", CancellationToken.None);
        await fetcher.GetStringAsync("http://fig/mag", CancellationToken.None);

        Assert.Equal(["http://fig/rg", "http://fig/mag"], inner.Requested);
    }

    /// <summary>Eight feeds on a dead page fail from one request rather than asking it eight times.</summary>
    [Fact]
    public async Task A_failed_fetch_is_shared_too()
    {
        var inner = new GatedFetcher(null);
        var fetcher = new CachingFetcher(inner);
        inner.Release();

        Assert.Null(await fetcher.GetStringAsync("http://fig/rg", CancellationToken.None));
        Assert.Null(await fetcher.GetStringAsync("http://fig/rg", CancellationToken.None));
        Assert.Single(inner.Requested);
    }

    /// <summary>The runner picks a fetcher by name, so the wrapper must answer to the wrapped one's.</summary>
    [Fact]
    public void Name_is_the_inner_fetchers()
    {
        Assert.Equal("Gated", new CachingFetcher(new GatedFetcher("page")).Name);
    }
}
