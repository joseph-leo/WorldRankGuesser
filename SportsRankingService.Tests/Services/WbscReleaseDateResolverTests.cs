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
        Assert.Equal(expected, WbscReleaseDateResolver.ExtractLatestReleaseDate(Fixture.Read("Wbsc_Rankings.html"), sportId));
    }

    [Fact]
    public void Throws_when_the_sport_has_no_dates()
    {
        Assert.ThrowsAny<Exception>(() => WbscReleaseDateResolver.ExtractLatestReleaseDate(Fixture.Read("Wbsc_Rankings.html"), "cricket-m"));
    }
}
