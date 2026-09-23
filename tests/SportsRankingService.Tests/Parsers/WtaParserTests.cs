using SportsRankingService.Parsers;

namespace SportsRankingService.Tests.Parsers;

public class WtaParserTests
{
    private readonly WtaParser _parser = new();

    [Fact]
    public void Parses_the_doubles_ranking()
    {
        var rows = _parser.Parse(Fixture.Read("Wta_Doubles.json")).Entries;

        Assert.Equal(100, rows.Count);
        Assert.Equal("CZE", rows.Single(r => r.Position == 1).ISO3);
        Assert.Equal("ROU", rows.Single(r => r.Position == 100).ISO3);
        Assert.All(rows, r => Assert.Matches("^[A-Z]{3}$", r.ISO3));
    }

    [Fact]
    public void Carries_the_rankedAt_date()
    {
        Assert.Equal(new DateOnly(2026, 9, 14), _parser.Parse(Fixture.Read("Wta_Doubles.json")).RankingDate);
    }

    [Fact]
    public void Carries_points_and_the_player_as_competitor_with_no_country_name()
    {
        var top = _parser.Parse(Fixture.Read("Wta_Doubles.json")).Entries.Single(r => r.Position == 1);

        Assert.Equal(11460m, top.Points);
        Assert.Equal("Katerina Siniakova", top.Competitor);
    }

    /// <summary>
    /// Deep in the list a few players carry no nationality: countryCode null, or "NCD" (no country declared; neither an
    /// ISO nor an IOC code), both seen 2026-09-22. A row without a country cannot count for one.
    /// </summary>
    [Theory]
    [InlineData("null")]
    [InlineData("\"NCD\"")]
    public void A_player_without_a_country_is_dropped(string countryCodeJson)
    {
        string json = $$"""
            [{"player":{"id":1,"countryCode":"FRA","fullName":"Ann"},"ranking":1415,"points":10,"rankedAt":"2026-09-14T00:00:00Z"},
             {"player":{"id":2,"countryCode":{{countryCodeJson}},"fullName":"Carolann Delaunay"},"ranking":1416,"points":10,"rankedAt":"2026-09-14T00:00:00Z"},
             {"player":{"id":3,"countryCode":"USA","fullName":"Bea"},"ranking":1417,"points":9,"rankedAt":"2026-09-14T00:00:00Z"}]
            """;

        var rows = _parser.Parse(json).Entries;

        Assert.Equal([1415, 1417], rows.Select(r => (int)r.Position));
    }
}
