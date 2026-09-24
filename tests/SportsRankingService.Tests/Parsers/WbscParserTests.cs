using SportsRankingService.Parsers;

namespace SportsRankingService.Tests.Parsers;

public class WbscParserTests
{
    private readonly WbscParser _parser = new();

    [Fact]
    public void Parses_every_ranked_team()
    {
        var rows = _parser.Parse(Sample.Read("Wbsc_Baseball_Men.json")).Entries;

        Assert.Equal(12, rows.Count);
        Assert.Equal("JPN", rows.Single(r => r.Position == 1).ISO3);
        Assert.Equal("TUR", rows.Last().ISO3);   // positions tie at the bottom, so 11 is shared
    }

    [Fact]
    public void Converts_IOC_codes_to_ISO3()
    {
        var codes = _parser.Parse(Sample.Read("Wbsc_Baseball_Men.json")).Entries.Select(r => r.ISO3).ToList();

        Assert.All(codes, c => Assert.Matches("^[A-Z]{3}$", c));
        Assert.Contains("DEU", codes);
        Assert.Contains("NLD", codes);
        Assert.DoesNotContain("GER", codes);
        Assert.DoesNotContain("NED", codes);
    }

    [Fact]
    public void Carries_the_release_date_of_the_rows()
    {
        Assert.Equal(new DateOnly(2026, 3, 26), _parser.Parse(Sample.Read("Wbsc_Baseball_Men.json")).RankingDate);
    }

    [Fact]
    public void Carries_points()
    {
        Assert.Equal(6337m, _parser.Parse(Sample.Read("Wbsc_Baseball_Men.json")).Entries.Single(r => r.Position == 1).Points);
    }
}
