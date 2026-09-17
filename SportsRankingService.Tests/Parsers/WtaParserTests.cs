using SportsRankingService.Parsers;

namespace SportsRankingService.Tests.Parsers;

public class WtaParserTests
{
    private readonly WtaParser _parser = new();

    [Fact]
    public void Parses_the_doubles_ranking()
    {
        var rows = _parser.Parse(Fixture.Read("Wta_Doubles.json")).Entries;

        Assert.Equal(100, rows.Count);
        Assert.Equal("CZE", rows.Single(r => r.Position == 1).ISO3);
        Assert.Equal("ROU", rows.Single(r => r.Position == 100).ISO3);
        Assert.All(rows, r => Assert.Matches("^[A-Z]{3}$", r.ISO3));
    }

    [Fact]
    public void Carries_the_rankedAt_date()
    {
        Assert.Equal(new DateOnly(2026, 9, 14), _parser.Parse(Fixture.Read("Wta_Doubles.json")).RankingDate);
    }

    [Fact]
    public void Carries_points_and_the_player_as_competitor_with_no_country_name()
    {
        var top = _parser.Parse(Fixture.Read("Wta_Doubles.json")).Entries.Single(r => r.Position == 1);

        Assert.Equal(11460m, top.Points);
        Assert.Equal("Katerina Siniakova", top.Competitor);
        Assert.Null(top.TeamName);
    }
}
