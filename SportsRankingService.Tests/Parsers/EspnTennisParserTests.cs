using SportsRankingService.Parsers;

namespace SportsRankingService.Tests.Parsers;

public class EspnTennisParserTests
{
    private readonly EspnTennisParser _parser = new();

    [Fact]
    public void Parses_the_singles_ranking()
    {
        var rows = _parser.Parse(Fixture.Read("Espn_Atp_Singles.json")).Entries;

        Assert.Equal(150, rows.Count);
        Assert.Equal("ITA", rows.Single(r => r.Position == 1).ISO3);
        Assert.Equal("LUX", rows.Single(r => r.Position == 150).ISO3);
        Assert.All(rows, r => Assert.Matches("^[A-Z]{3}$", r.ISO3));
    }

    [Fact]
    public void Carries_the_ranking_groups_update_date()
    {
        Assert.Equal(new DateOnly(2026, 9, 10), _parser.Parse(Fixture.Read("Espn_Atp_Singles.json")).RankingDate);
    }

    [Fact]
    public void Carries_points_and_the_player_as_competitor_with_no_country_name()
    {
        var top = _parser.Parse(Fixture.Read("Espn_Atp_Singles.json")).Entries.Single(r => r.Position == 1);

        Assert.Equal(11500m, top.Points);
        Assert.Equal("Jannik Sinner", top.Competitor);
        Assert.Null(top.TeamName);
    }
}
