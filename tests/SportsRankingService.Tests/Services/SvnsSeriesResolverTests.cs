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
        var ids = SvnsSeriesResolver.ExtractSeriesIds(Sample.Read("Svns_Standings.html"));

        Assert.Equal(["11111111-2222-4333-8444-555555555555", "66666666-7777-4888-9999-000000000000"], ids);
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
        Assert.Equal(expected, SvnsSeriesResolver.ReadSportCode(Sample.Read(fixture)));
    }
}
