using SportsRankingService.Services.UrlResolvers;

namespace SportsRankingService.Tests.Services;

/// <summary>
/// svns.com/en/standings carries the current season's series ids in a data attribute; the
/// World Rugby series endpoint then says whether a series is men's ("mrs") or women's ("wrs").
/// </summary>
public class SvnsSeriesResolverTests
{
    [Fact]
    public void Extracts_the_series_ids_from_the_standings_page()
    {
        var ids = SvnsSeriesResolver.ExtractSeriesIds(Fixture.Read("Svns_Standings.html"));

        Assert.Equal(["1cbf9c53-7b2f-4973-9619-215c98d7a494", "60023731-b2e9-4d5f-8c19-e6ecf2cf1e89"], ids);
    }

    [Fact]
    public void Throws_when_the_page_has_no_series_ids()
    {
        Assert.ThrowsAny<Exception>(() => SvnsSeriesResolver.ExtractSeriesIds("<html><body>nope</body></html>"));
    }

    [Theory]
    [InlineData("Svns_Series_Men.json", "mrs")]
    [InlineData("Svns_Series_Women.json", "wrs")]
    public void Reads_the_sport_code_from_the_series_metadata(string fixture, string expected)
    {
        Assert.Equal(expected, SvnsSeriesResolver.ReadSportCode(Fixture.Read(fixture)));
    }
}
