using SportsRankingService.Parsers;
using SportsRankingService.Parsing;

namespace SportsRankingService.Tests.Parsers;

/// <summary>
/// BWF ranking table API. Countries come as names only (p1_country_model.name), so every name in
/// a fixture must resolve through CountryUtil; an unresolvable name fails the parse loudly.
/// Doubles pairs yield one entry per partner with the same position.
/// </summary>
public class BwfParserTests
{
    private readonly BwfParser _parser = new();

    [Fact]
    public void Singles_yield_one_entry_per_player()
    {
        var rows = _parser.Parse(Fixture.Read("Bwf_MensSingles.json")).Entries;

        Assert.Equal(100, rows.Count);
        Assert.Equal("IDN", rows.Single(r => r.Position == 1).ISO3);
        Assert.Equal("AZE", rows.Single(r => r.Position == 100).ISO3);
        Assert.All(rows, r => Assert.Matches("^[A-Z]{3}$", r.ISO3));
    }

    [Fact]
    public void Doubles_yield_one_entry_per_partner_with_the_pairs_position()
    {
        var rows = _parser.Parse(Fixture.Read("Bwf_MensDoubles.json")).Entries;

        Assert.Equal(new[] { "KOR", "KOR" }, rows.Where(r => r.Position == 1).Select(r => r.ISO3));
        Assert.Equal(new[] { "KOR", "MYS" }, rows.Where(r => r.Position == 37).Select(r => r.ISO3).Order());
        Assert.All(rows, r => Assert.Matches("^[A-Z]{3}$", r.ISO3));
    }

    [Fact]
    public void Neutral_athletes_are_dropped()
    {
        // The doubles fixture contains "Athlete Independent Neutral" players; the singles one does not.
        var rows = _parser.Parse(Fixture.Read("Bwf_MensDoubles.json")).Entries;

        Assert.InRange(rows.Count, 190, 199);
    }

    [Fact]
    public void An_unknown_country_name_fails_the_parse_naming_it()
    {
        const string json = """{"results":{"data":[{"rank":1,"player1_model":{"slug":"x"},"p1_country_model":{"name":"Atlantis"}}]}}""";

        var ex = Assert.Throws<ParseException>(() => _parser.Parse(json));

        Assert.Contains("Atlantis", ex.Message);
    }

    [Fact]
    public void Singles_carry_points_the_country_name_and_the_player_as_competitor()
    {
        var top = _parser.Parse(Fixture.Read("Bwf_MensSingles.json")).Entries.Single(r => r.Position == 1);

        Assert.Equal(87631m, top.Points);
        Assert.Equal("Indonesia", top.TeamName);
        Assert.Equal("Jonatan CHRISTIE", top.Competitor);
    }

    [Fact]
    public void Doubles_partners_share_the_pair_as_competitor()
    {
        var pair = _parser.Parse(Fixture.Read("Bwf_MensDoubles.json")).Entries.Where(r => r.Position == 1).ToList();

        Assert.All(pair, r => Assert.Equal("KIM Won Ho / SEO Seung Jae", r.Competitor));
        Assert.All(pair, r => Assert.Equal(114099m, r.Points));
    }
}
