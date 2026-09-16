using SportsRankingService.Parsers;

namespace SportsRankingService.Tests.Parsers;

public class FihParserTests
{
    private readonly FihParser _parser = new();

    [Fact]
    public void Parses_every_ranked_team()
    {
        var rows = _parser.Parse(Fixture.Read("Fih_Outdoor_Men.json")).Entries;

        Assert.Equal(104, rows.Count);
        Assert.Equal("DEU", rows.Single(r => r.Position == 1).ISO3);
        Assert.Equal("MAR", rows.Single(r => r.Position == 104).ISO3);
    }

    [Fact]
    public void Converts_IOC_codes_to_ISO3()
    {
        var rows = _parser.Parse(Fixture.Read("Fih_Outdoor_Men.json")).Entries;

        Assert.All(rows, r => Assert.Matches("^[A-Z]{3}$", r.ISO3));
        Assert.DoesNotContain(rows, r => r.ISO3 == "GER");
    }
}
