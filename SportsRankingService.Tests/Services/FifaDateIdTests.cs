using SportsRankingService.Models;
using SportsRankingService.Services;
using SportsRankingService.Services.UrlResolvers;

namespace SportsRankingService.Tests.Services;

/// <summary>
/// FIFA's ranking pages embed the available ranking dates as Next.js page data.
/// The worker needs the id of the newest date to build the API URL.
/// </summary>
public class FifaDateIdTests
{
    [Fact]
    public void Picks_the_newest_date_id_from_the_real_page()
    {
        string id = FifaDateIdResolver.ExtractLatestDateId(Fixture.Read("Fifa_WorldRanking_Men.html"));

        Assert.Equal("FRS_Male_Football_20260611", id);
    }

    [Fact]
    public void Picks_the_newest_womens_date_id_from_the_womens_page()
    {
        string id = FifaDateIdResolver.ExtractLatestDateId(Fixture.Read("Fifa_WorldRanking_Women.html"));

        Assert.Equal("FRS_Female_Football_20260419", id);
    }

    [Fact]
    public void Picks_by_iso_date_not_by_position_in_the_list()
    {
        const string html = """
            <html><body><script id="__NEXT_DATA__" type="application/json">
            {"props":{"pageProps":{"pageData":{"ranking":{"dates":[
              {"year":"2026","dates":[
                {"id":"older","iso":"2026-01-19T17:11:57.976Z","dateText":"19 January"},
                {"id":"newest","iso":"2026-07-20T08:37:28.979Z","dateText":"20 July"}]},
              {"year":"2025","dates":[
                {"id":"id14870","iso":"2025-09-18T00:00:00.000Z","dateText":"18 September"}]}
            ]}}}}}
            </script></body></html>
            """;

        Assert.Equal("newest", FifaDateIdResolver.ExtractLatestDateId(html));
    }

    [Fact]
    public void Throws_when_the_page_has_no_dates()
    {
        Assert.ThrowsAny<Exception>(() => FifaDateIdResolver.ExtractLatestDateId("<html><body>nothing here</body></html>"));
    }

    [Fact]
    public void The_ranking_date_is_the_iso_timestamp_of_the_newest_entry_not_the_digits_in_its_id()
    {
        // The men's page's newest entry is id FRS_Male_Football_20260611 with iso 2026-07-20; FIFA displays 20 July.
        SoccerRankDate latest = FifaDateIdResolver.ExtractLatestDate(Fixture.Read("Fifa_WorldRanking_Men.html"));

        Assert.Equal("FRS_Male_Football_20260611", latest.id);
        Assert.Equal(new DateOnly(2026, 7, 20), FifaDateIdResolver.ToRankingDate(latest));
    }

    [Fact]
    public async Task Resolve_formats_the_id_into_the_url_and_returns_the_ranking_date()
    {
        var fetcher = new FakeFetcher(new() { ["https://inside.fifa.com/fifa-rankings/world-ranking/men"] = Fixture.Read("Fifa_WorldRanking_Men.html") });
        var item = new RankingItem { Sport = "Soccer", Gender = "Men", Url = "http://fifa/api?id={0}", Source = "FifaV3", UrlResolver = "FifaDateId" };

        ResolvedUrl resolved = await new FifaDateIdResolver(fetcher).ResolveAsync(item, CancellationToken.None);

        Assert.Equal("http://fifa/api?id=FRS_Male_Football_20260611", resolved.Url);
        Assert.Equal(new DateOnly(2026, 7, 20), resolved.RankingDate);
    }

    private sealed class FakeFetcher(Dictionary<string, string> pages) : IHttpFetcher
    {
        public string Name => HttpFetcher.FetcherName;

        public Task<string?> GetStringAsync(string url, CancellationToken cancellationToken) =>
            Task.FromResult(pages.GetValueOrDefault(url));
    }
}
