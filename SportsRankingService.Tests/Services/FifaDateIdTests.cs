using SportsRankingService.Services;

namespace SportsRankingService.Tests.Services;

/// <summary>
/// FIFA's ranking page embeds the available ranking dates as Next.js page data.
/// The worker needs the id of the newest date to build the API URL.
/// </summary>
public class FifaDateIdTests
{
    [Fact]
    public void Picks_the_newest_date_id_from_the_real_page()
    {
        string id = WorldRankService.ExtractLatestDateId(Fixture.Read("Fifa_WorldRanking_Men.html"));

        Assert.Equal("FRS_Male_Football_20260611", id);
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

        Assert.Equal("newest", WorldRankService.ExtractLatestDateId(html));
    }

    [Fact]
    public void Throws_when_the_page_has_no_dates()
    {
        Assert.ThrowsAny<Exception>(() => WorldRankService.ExtractLatestDateId("<html><body>nothing here</body></html>"));
    }
}
