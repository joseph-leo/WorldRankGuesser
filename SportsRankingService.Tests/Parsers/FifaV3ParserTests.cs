using SportsRankingService.Parsers;

namespace SportsRankingService.Tests.Parsers;

public class FifaV3ParserTests
{
    private readonly FifaV3Parser _parser = new();

    [Fact]
    public void Parses_the_mens_ranking()
    {
        var rows = _parser.Parse(Fixture.Read("Fifa_V3_Men_FRS_20260611.json")).Entries;

        Assert.Equal(211, rows.Count);
        Assert.Equal("ESP", rows.Single(r => r.Position == 1).ISO3);
        Assert.Equal("SMR", rows.Single(r => r.Position == 211).ISO3);
    }

    [Fact]
    public void Parses_the_womens_ranking_and_skips_unranked_teams()
    {
        // The fixture lists 204 teams; the last 6 have a null rank.
        var rows = _parser.Parse(Fixture.Read("Fifa_V3_Women_FRS_20260419.json")).Entries;

        Assert.Equal(198, rows.Count);
        Assert.Equal("ESP", rows.Single(r => r.Position == 1).ISO3);
        Assert.Equal("MUS", rows.Single(r => r.Position == 198).ISO3);   // MRI
    }

    [Fact]
    public void Codes_are_three_letters_and_IOC_style_codes_are_converted()
    {
        var codes = _parser.Parse(Fixture.Read("Fifa_V3_Men_FRS_20260611.json")).Entries.Select(r => r.ISO3).ToList();

        Assert.All(codes, c => Assert.Matches("^[A-Z]{3}$", c));
        Assert.Contains("DEU", codes);
        Assert.DoesNotContain("GER", codes);
        Assert.Contains("ENG", codes);   // FIFA home nations are kept as FIFA emits them
    }

    [Fact]
    public void Carries_total_points_and_the_team_name()
    {
        var top = _parser.Parse(Fixture.Read("Fifa_V3_Men_FRS_20260611.json")).Entries.Single(r => r.Position == 1);

        Assert.Equal(1995.881879m, top.Points);
        Assert.Equal("Spain", top.TeamName);
    }
}
