using SportsRankingService.Parsers;

namespace SportsRankingService.Tests.Parsers;

public class FifaV3ParserTests
{
    private readonly FifaV3Parser _parser = new();

    [Fact]
    public void Parses_the_mens_ranking()
    {
        var rows = _parser.Parse(Sample.Read("Fifa_V3_Men.json")).Entries;

        Assert.Equal(12, rows.Count);
        Assert.Equal("ESP", rows.Single(r => r.Position == 1).ISO3);
        Assert.Equal("SMR", rows.Single(r => r.Position == 12).ISO3);
    }

    [Fact]
    public void Parses_the_womens_ranking_and_skips_unranked_teams()
    {
        // The sample lists 10 teams; the last 2 have a null rank.
        var rows = _parser.Parse(Sample.Read("Fifa_V3_Women.json")).Entries;

        Assert.Equal(8, rows.Count);
        Assert.Equal("ESP", rows.Single(r => r.Position == 1).ISO3);
        Assert.Equal("MUS", rows.Single(r => r.Position == 8).ISO3);   // MRI
    }

    [Fact]
    public void Codes_are_three_letters_and_IOC_style_codes_are_converted()
    {
        var codes = _parser.Parse(Sample.Read("Fifa_V3_Men.json")).Entries.Select(r => r.ISO3).ToList();

        Assert.All(codes, c => Assert.Matches("^[A-Z]{3}$", c));
        Assert.Contains("DEU", codes);
        Assert.DoesNotContain("GER", codes);
        Assert.Contains("ENG", codes);   // FIFA home nations are kept as FIFA emits them
    }

    [Fact]
    public void Carries_total_points_and_the_team_name()
    {
        var top = _parser.Parse(Sample.Read("Fifa_V3_Men.json")).Entries.Single(r => r.Position == 1);

        Assert.Equal(1995.881879m, top.Points);
        Assert.Equal("ESP", top.ISO3);
    }
}
