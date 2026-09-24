using SportsRankingService.Parsers;

namespace SportsRankingService.Tests.Parsers;

public class SvnsParserTests
{
    private readonly SvnsParser _parser = new();

    [Fact]
    public void Parses_the_mens_series_standings()
    {
        var rows = _parser.Parse(Sample.Read("Svns_Standings_Men.json")).Entries;

        Assert.Equal(12, rows.Count);
        Assert.Equal("ZAF", rows.Single(r => r.Position == 1).ISO3);   // RSA
        Assert.Equal("URY", rows.Single(r => r.Position == 12).ISO3);  // URU
    }

    [Fact]
    public void Parses_the_womens_series_standings_with_the_same_shape()
    {
        var rows = _parser.Parse(Sample.Read("Svns_Standings_Women.json")).Entries;

        Assert.Equal(12, rows.Count);
        Assert.Equal("AUS", rows.Single(r => r.Position == 1).ISO3);
        Assert.Equal("ZAF", rows.Single(r => r.Position == 12).ISO3);
        Assert.All(rows, r => Assert.Matches("^[A-Z]{3}$", r.ISO3));
    }

    [Fact]
    public void Carries_total_points()
    {
        Assert.Equal(52m, _parser.Parse(Sample.Read("Svns_Standings_Men.json")).Entries.Single(r => r.Position == 1).Points);
    }
}
