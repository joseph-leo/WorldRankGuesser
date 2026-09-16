using SportsRankingService.Parsers;

namespace SportsRankingService.Tests.Parsers;

public class EspnTennisParserTests
{
    private readonly EspnTennisParser _parser = new();

    [Fact]
    public void Parses_the_singles_ranking()
    {
        var rows = _parser.Parse(Fixture.Read("Espn_Atp_Singles.json"));

        Assert.Equal(150, rows.Count);
        Assert.Equal("ITA", rows.Single(r => r.Position == 1).ISO3);
        Assert.Equal("LUX", rows.Single(r => r.Position == 150).ISO3);
        Assert.All(rows, r => Assert.Matches("^[A-Z]{3}$", r.ISO3));
    }
}
