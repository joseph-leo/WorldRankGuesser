using SportsRankingService.Parsers;
using SportsRankingService.Parsing;

namespace SportsRankingService.Tests.Parsers;

/// <summary>
/// BWF ranking table API. Countries come as names only (p1_country_model.name), so every name in
/// a fixture must resolve through CountryUtil; an unresolvable name fails the parse loudly.
/// Doubles pairs yield one entry per partner, each naming its own partner.
/// </summary>
public class BwfParserTests
{
    private readonly BwfParser _parser = new();

    [Fact]
    public void Singles_yield_one_entry_per_player()
    {
        var rows = _parser.Parse(Sample.Read("Bwf_MensSingles.json")).Entries;

        Assert.Equal(12, rows.Count);
        Assert.Equal("IDN", rows.Single(r => r.Position == 1).ISO3);
        Assert.Equal("AZE", rows.Single(r => r.Position == 12).ISO3);
        Assert.All(rows, r => Assert.Matches("^[A-Z]{3}$", r.ISO3));
    }

    [Fact]
    public void Doubles_yield_one_entry_per_partner_with_the_pairs_position()
    {
        var rows = _parser.Parse(Sample.Read("Bwf_MensDoubles.json")).Entries;

        Assert.Equal(new[] { "KOR", "KOR" }, rows.Where(r => r.Position == 1).Select(r => r.ISO3));
        Assert.Equal(new[] { "KOR", "MYS" }, rows.Where(r => r.Position == 5).Select(r => r.ISO3).Order());
        Assert.All(rows, r => Assert.Matches("^[A-Z]{3}$", r.ISO3));
    }

    [Fact]
    public void Neutral_athletes_are_dropped()
    {
        // The doubles sample has ten pairs, one of them "Athlete Independent Neutral"; the singles one has none.
        var rows = _parser.Parse(Sample.Read("Bwf_MensDoubles.json")).Entries;

        Assert.Equal(18, rows.Count);
    }

    /// <summary>
    /// A player competing independently of their federation is labelled "&lt;COUNTRY&gt; Independent" (men's singles,
    /// 2026-09-22). Unlike a neutral athlete they still have a country, and they count for it.
    /// </summary>
    [Fact]
    public void An_independent_player_counts_for_the_country_in_the_label()
    {
        const string json = """{"results":{"data":[{"rank":1200,"player1_model":{"slug":"x"},"p1_country_model":{"name":"MOROCCO Independent"}}]}}""";

        var rows = _parser.Parse(json).Entries;

        Assert.Equal("MAR", Assert.Single(rows).ISO3);
    }

    [Fact]
    public void An_unknown_country_name_fails_the_parse_naming_it()
    {
        const string json = """{"results":{"data":[{"rank":1,"player1_model":{"slug":"x"},"p1_country_model":{"name":"Atlantis"}}]}}""";

        var ex = Assert.Throws<ParseException>(() => _parser.Parse(json));

        Assert.Contains("Atlantis", ex.Message);
    }

    /// <summary>
    /// BWF's full lists echo some rows inside a block of tied players: a second row id for the same player ids with the same
    /// rank and points (172 echoes over the five lists on 2026-09-18). The sample is five rows tied at rank 925: three players,
    /// two of them listed twice.
    /// </summary>
    [Fact]
    public void A_player_the_feed_lists_twice_is_one_entry()
    {
        var rows = _parser.Parse(Sample.Read("Bwf_MensSingles_RepeatedRows.json")).Entries;

        Assert.Equal(["PER", "DOM", "CAN"], rows.Select(r => r.ISO3));
        Assert.All(rows, r => Assert.Equal((short)925, r.Position));
    }

    /// <summary>
    /// A player can be ranked with several partners. When two of his pairs tie on rank and points his two entries are
    /// identical, because an entry names the player and not the pair; he is then listed once (decided 2026-09-18).
    /// The sample is one player's two pairs tied at rank 505.
    /// </summary>
    [Fact]
    public void A_player_in_two_tied_pairs_is_listed_once_at_that_rank()
    {
        var rows = _parser.Parse(Sample.Read("Bwf_MensDoubles_TiedPairs.json")).Entries;

        Assert.Equal(["Sam INDIA", "Noa JULIET", "Eli KILO"], rows.Select(r => r.Competitor));
        Assert.All(rows, r => Assert.Equal(new RankEntry(505, "USA", r.Competitor, 2200m), r));
    }

    [Fact]
    public void A_repeated_player_whose_rows_disagree_fails_the_parse()
    {
        const string json = """
            {"results":{"data":[
              {"rank":7,"points":"500.0000","player1_id":11,"player2_id":null,"player1_model":{"slug":"x"},"p1_country_model":{"name":"Peru"}},
              {"rank":9,"points":"500.0000","player1_id":11,"player2_id":null,"player1_model":{"slug":"x"},"p1_country_model":{"name":"Peru"}}]}}
            """;

        var ex = Assert.Throws<ParseException>(() => _parser.Parse(json));

        Assert.Contains("11", ex.Message);
    }

    [Fact]
    public void Singles_carry_points_the_country_name_and_the_player_as_competitor()
    {
        var top = _parser.Parse(Sample.Read("Bwf_MensSingles.json")).Entries.Single(r => r.Position == 1);

        Assert.Equal(87631m, top.Points);
        Assert.Equal("IDN", top.ISO3);
        Assert.Equal("Jonah ALPHA", top.Competitor);
    }

    [Fact]
    public void Doubles_partners_each_carry_their_own_name()
    {
        var pair = _parser.Parse(Sample.Read("Bwf_MensDoubles.json")).Entries.Where(r => r.Position == 1).ToList();

        Assert.Equal(["Jonah ALPHA", "Mika BRAVO"], pair.Select(r => r.Competitor));
        Assert.All(pair, r => Assert.Equal("KOR", r.ISO3));
        Assert.All(pair, r => Assert.Equal(114099m, r.Points));
    }
}
