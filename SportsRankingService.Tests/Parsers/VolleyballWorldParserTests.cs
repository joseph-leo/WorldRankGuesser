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
}
