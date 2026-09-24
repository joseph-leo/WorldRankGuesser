using SportsRankingService.Models;
using SportsRankingService.Services;
using SportsRankingService.Services.UrlResolvers;

namespace SportsRankingService.Tests.Services;

/// <summary>
/// rankings.wbsc.org embeds its release-date dropdown as {"date","sport","year","formatted"}
/// objects. The rankings API only answers for an exact release date, so the newest per sport id is needed.
/// </summary>
public class WbscReleaseDateResolverTests
{
    [Theory]
    [InlineData("baseball-m", "2026-03-26")]
    [InlineData("baseball-w", "2026-09-09")]
    [InlineData("softball-m", "2026-05-14")]
    [InlineData("softball-w", "2025-12-31")]
    [InlineData("baseball5-coed", "2026-08-07")]
    public void Picks_the_newest_release_date_for_the_sport(string sportId, string expected)
    {
        Assert.Equal(expected, WbscReleaseDateResolver.ExtractLatestReleaseDate(Sample.Read("Wbsc_Rankings.html"), sportId));
    }

    [Fact]
    public void Throws_when_the_sport_has_no_dates()
    {
        Assert.ThrowsAny<Exception>(() => WbscReleaseDateResolver.ExtractLatestReleaseDate(Sample.Read("Wbsc_Rankings.html"), "cricket-m"));
    }

    [Fact]
    public async Task Resolve_formats_the_newest_date_into_the_url_and_returns_it()
    {
        var fetcher = new FakeFetcher(new() { ["https://rankings.wbsc.org/"] = Sample.Read("Wbsc_Rankings.html") });
        var item = new RankingItem { Sport = "Baseball", Gender = "Men", Url = "http://wbsc/api?sportId=baseball-m&date={0}", Source = "Wbsc", UrlResolver = "WbscReleaseDate" };

        ResolvedUrl resolved = await new WbscReleaseDateResolver(fetcher).ResolveAsync(item, CancellationToken.None);

        Assert.Equal("http://wbsc/api?sportId=baseball-m&date=2026-03-26", resolved.Url);
        Assert.Equal(new DateOnly(2026, 3, 26), resolved.RankingDate);
    }

    private sealed class FakeFetcher(Dictionary<string, string> pages) : IHttpFetcher
    {
        public string Name => HttpFetcher.FetcherName;

        public Task<string?> GetStringAsync(string url, CancellationToken cancellationToken) =>
            Task.FromResult(pages.GetValueOrDefault(url));
    }
}
