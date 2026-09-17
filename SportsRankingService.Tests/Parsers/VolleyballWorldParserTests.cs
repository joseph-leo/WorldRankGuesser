using SportsRankingService.Parsers;

namespace SportsRankingService.Tests.Parsers;

public class VolleyballWorldParserTests
{
    private readonly VolleyballWorldParser _parser = new();

    [Fact]
    public void Parses_every_ranked_team()
    {
        var rows = _parser.Parse(Fixture.Read("VolleyballWorld_Men.json")).Entries;

        Assert.Equal(100, rows.Count);
        Assert.Equal("POL", rows.Single(r => r.Position == 1).ISO3);
        Assert.Equal("BIH", rows.Single(r => r.Position == 100).ISO3);
        Assert.All(rows, r => Assert.Matches("^[A-Z]{3}$", r.ISO3));
    }

    [Fact]
    public void Has_no_federation_ranking_date()
    {
        Assert.Null(_parser.Parse(Fixture.Read("VolleyballWorld_Men.json")).RankingDate);
    }

    [Fact]
    public void Carries_the_decimal_points()
    {
        Assert.Equal(408.9m, _parser.Parse(Fixture.Read("VolleyballWorld_Men.json")).Entries.Single(r => r.Position == 1).Points);
    }

    [Fact]
    public void Beach_ranks_pairs_with_the_pair_as_competitor_and_integer_points()
    {
        var rows = _parser.Parse(Fixture.Read("VolleyballWorld_Beach_Men.json")).Entries;
        var top = rows.Single(r => r.Position == 1);

        Assert.Equal("SWE", top.ISO3);
        Assert.Equal("Sweden", top.TeamName);
        Assert.Equal("Hölting Nilsson/Andersson, E", top.Competitor);
        Assert.Equal(8020m, top.Points);
        Assert.Contains(rows, r => r.ISO3 == "BRA" && r.Position != rows.First(b => b.ISO3 == "BRA").Position);   // a country fields several pairs
    }

    [Fact]
    public void Indoor_teams_are_countries_with_no_competitor()
    {
        Assert.Null(_parser.Parse(Fixture.Read("VolleyballWorld_Men.json")).Entries.Single(r => r.Position == 1).Competitor);
    }
}
