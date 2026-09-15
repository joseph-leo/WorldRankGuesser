using Microsoft.Extensions.Logging.Abstractions;
using SportsRankingService.Models;
using SportsRankingService.Parsers;

namespace SportsRankingService.Tests.Parsers;

public class TennisParserTests
{
    private readonly TennisParser _parser = new(NullLogger<TennisParser>.Instance);

    private static readonly RankingItem SinglesMen = new() { Sport = "Tennis", Event = "Singles", Gender = "Men", Url = "" };
    private static readonly RankingItem DoublesWomen = new() { Sport = "Tennis", Event = "Doubles", Gender = "Women", Url = "" };

    [Fact]
    public void Singles_parses_the_ESPN_feed()
    {
        var rows = _parser.ParseResponse(Fixture.Read("Espn_Atp_Singles.json"), SinglesMen).ToList();

        Assert.Equal(150, rows.Count);
        Assert.Equal("ITA", rows.Single(r => r.Position == 1).ISO3);
        Assert.Equal("LUX", rows.Single(r => r.Position == 150).ISO3);
        Assert.All(rows, r => Assert.Matches("^[A-Z]{3}$", r.ISO3));
    }

    [Fact]
    public void WomensDoubles_parses_the_WTA_feed()
    {
        var rows = _parser.ParseResponse(Fixture.Read("Wta_Doubles.json"), DoublesWomen).ToList();

        Assert.Equal(100, rows.Count);
        Assert.Equal("CZE", rows.Single(r => r.Position == 1).ISO3);
        Assert.Equal("ROU", rows.Single(r => r.Position == 100).ISO3);
        Assert.All(rows, r => Assert.Matches("^[A-Z]{3}$", r.ISO3));
    }

    [Fact(Skip = "No fixture: https://www.atptour.com/en/rankings/doubles returns 403 to a scripted client (captured 2026-09-15). The ESPN API used for singles likely serves doubles too.")]
    public void MensDoubles_parses_the_ATP_page()
    {
    }
}
