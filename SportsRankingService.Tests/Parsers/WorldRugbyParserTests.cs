using SportsRankingService.Parsers;

namespace SportsRankingService.Tests.Parsers;

public class WorldRugbyParserTests
{
    private readonly WorldRugbyParser _parser = new();

    [Fact]
    public void Parses_every_ranked_team()
    {
        var rows = _parser.Parse(Fixture.Read("WorldRugby_Union_Men.json")).Entries;

        Assert.Equal(114, rows.Count);
        Assert.Equal("ZAF", rows.Single(r => r.Position == 1).ISO3);
        Assert.Equal("ASM", rows.Single(r => r.Position == 114).ISO3);
        Assert.All(rows, r => Assert.Matches("^[A-Z]{3}$", r.ISO3));
    }

    [Fact]
    public void Carries_the_effective_ranking_date()
    {
        var ranking = _parser.Parse(Fixture.Read("WorldRugby_Union_Men.json"));

        Assert.Equal(new DateOnly(2026, 9, 14), ranking.RankingDate);
    }

    [Fact]
    public void Carries_points_and_the_team_name()
    {
        var top = _parser.Parse(Fixture.Read("WorldRugby_Union_Men.json")).Entries.Single(r => r.Position == 1);

        Assert.Equal(95.09175036626566m, top.Points);
        Assert.Equal("South Africa", top.TeamName);
    }
}
