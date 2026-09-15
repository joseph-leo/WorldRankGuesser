using Microsoft.Extensions.Logging.Abstractions;
using SportsRankingService.Models;
using SportsRankingService.Parsers;

namespace SportsRankingService.Tests.Parsers;

public class CricketParserTests
{
    private readonly CricketParser _parser = new(NullLogger<CricketParser>.Instance);

    private static readonly RankingItem TestMen = new() { Sport = "Cricket", Event = "Test", Gender = "Men", Url = "" };
    private static readonly RankingItem T20Women = new() { Sport = "Cricket", Event = "T20I", Gender = "Women", Url = "" };

    [Fact]
    public void Test_rankings_parse_every_team()
    {
        var rows = _parser.ParseResponse(Fixture.Read("Icc_Test_Men.json"), TestMen).ToList();

        Assert.Equal(10, rows.Count);
        Assert.Equal("AUS", rows.Single(r => r.Position == 1).ISO3);
        Assert.Equal("ZWE", rows.Single(r => r.Position == 10).ISO3);
    }

    [Fact]
    public void ICC_two_letter_team_codes_map_to_ISO3()
    {
        var codes = _parser.ParseResponse(Fixture.Read("Icc_Test_Men.json"), TestMen).Select(r => r.ISO3).ToList();

        Assert.Contains("ZAF", codes);   // SA
        Assert.Contains("NZL", codes);   // NZ
        Assert.Contains("LKA", codes);   // SL
        Assert.DoesNotContain("SA", codes);
        Assert.DoesNotContain("NZ", codes);
        Assert.DoesNotContain("SL", codes);
    }

    [Fact]
    public void West_Indies_has_no_ISO3_and_is_kept_as_WI()
    {
        var codes = _parser.ParseResponse(Fixture.Read("Icc_Test_Men.json"), TestMen).Select(r => r.ISO3).ToList();

        Assert.Contains("WI", codes);
    }

    [Fact]
    public void Womens_team_codes_drop_the_W_suffix()
    {
        var rows = _parser.ParseResponse(Fixture.Read("Icc_T20_Women.json"), T20Women).ToList();

        Assert.Equal(80, rows.Count);
        Assert.Equal("AUS", rows.First(r => r.Position == 1).ISO3);
        Assert.All(rows, r => Assert.DoesNotContain("-", r.ISO3));
        Assert.Contains("HKG", rows.Select(r => r.ISO3));   // HK-W
    }
}
