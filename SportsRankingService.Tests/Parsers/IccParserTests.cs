using SportsRankingService.Parsers;

namespace SportsRankingService.Tests.Parsers;

public class IccParserTests
{
    private readonly IccParser _parser = new();

    [Fact]
    public void Test_rankings_parse_every_team()
    {
        var rows = _parser.Parse(Fixture.Read("Icc_Test_Men.json")).Entries;

        Assert.Equal(10, rows.Count);
        Assert.Equal("AUS", rows.Single(r => r.Position == 1).ISO3);
        Assert.Equal("ZWE", rows.Single(r => r.Position == 10).ISO3);
    }

    [Fact]
    public void Two_letter_team_codes_map_to_ISO3()
    {
        var codes = _parser.Parse(Fixture.Read("Icc_Test_Men.json")).Entries.Select(r => r.ISO3).ToList();

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
        var codes = _parser.Parse(Fixture.Read("Icc_Test_Men.json")).Entries.Select(r => r.ISO3).ToList();

        Assert.Contains("WI", codes);
    }

    [Fact]
    public void Womens_team_codes_drop_the_W_suffix()
    {
        var rows = _parser.Parse(Fixture.Read("Icc_T20_Women.json")).Entries;

        Assert.Equal(80, rows.Count);
        Assert.Equal("AUS", rows.First(r => r.Position == 1).ISO3);
        Assert.All(rows, r => Assert.DoesNotContain("-", r.ISO3));
        Assert.Contains("HKG", rows.Select(r => r.ISO3));   // HK-W
    }

    [Fact]
    public void Carries_the_ICC_rank_date()
    {
        Assert.Equal(new DateOnly(2026, 9, 12), _parser.Parse(Fixture.Read("Icc_Test_Men.json")).RankingDate);
    }

    [Fact]
    public void Carries_the_rating_as_points()
    {
        // The ICC ranks by Rating; its Points field is the raw total the rating is derived from.
        Assert.Equal(126m, _parser.Parse(Fixture.Read("Icc_Test_Men.json")).Entries.Single(r => r.Position == 1).Points);
    }
}
