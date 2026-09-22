using SportsRankingService.Parsers;
using SportsRankingService.Parsing;

namespace SportsRankingService.Tests.Parsers;

/// <summary>
/// FIG's per-discipline ranking pages (ranking_mag_table.php, ranking_wag_table.php,
/// ranking_rg_table.php). One page holds two series, "World Cup" and "World Challenge Cup",
/// each with one tab per apparatus. A row is one athlete (or, for rhythmic group events, one
/// national group): rank, a flag image whose alt is the IOC code, and the name. FIG publishes
/// no country ranking and no ranking date. The selector "&lt;series&gt; / &lt;apparatus&gt;"
/// picks one table from the page.
/// </summary>
public class FigParserTests
{
    private readonly FigParser _parser = new();

    private static string Men => Fixture.Read("Fig_Artistic_Men.html");
    private static string Women => Fixture.Read("Fig_Artistic_Women.html");
    private static string Rhythmic => Fixture.Read("Fig_Rhythmic_Women.html");

    [Fact]
    public void Selects_the_apparatus_table_of_the_requested_series()
    {
        var worldCup = _parser.Parse(Men, "World Cup / Floor Exercise").Entries;
        var challengeCup = _parser.Parse(Men, "World Challenge Cup / Floor Exercise").Entries;

        Assert.Equal(104, worldCup.Count);
        Assert.Equal("BLR", worldCup.First(r => r.Position == 1).ISO3);
        Assert.Equal("PHL", worldCup.First(r => r.Position == 2).ISO3);
        Assert.Equal("ISR", worldCup.First(r => r.Position == 3).ISO3);

        Assert.Equal(55, challengeCup.Count);
        Assert.Equal("DEU", challengeCup.First(r => r.Position == 1).ISO3);
    }

    [Fact]
    public void Carries_the_athlete_as_competitor_with_plain_spaces_and_no_country_name()
    {
        var top = _parser.Parse(Men, "World Cup / Floor Exercise").Entries.First(r => r.Position == 1);

        Assert.Equal("SHARAMKOU Yahor", top.Competitor);
    }

    [Fact]
    public void Tied_athletes_keep_the_same_position()
    {
        var rows = _parser.Parse(Men, "World Challenge Cup / Horizontal Bar").Entries;

        var leaders = rows.Where(r => r.Position == 1).Select(r => r.ISO3).ToList();
        Assert.Equal(["BGR", "DEU"], leaders);
        Assert.DoesNotContain(rows, r => r.Position == 2);
    }

    [Fact]
    public void Womens_artistic_page_has_its_own_apparatus_set()
    {
        var rows = _parser.Parse(Women, "World Cup / Balance Beam").Entries;

        Assert.Equal(134, rows.Count);
        Assert.Equal("DZA", rows.First(r => r.Position == 1).ISO3);
        Assert.Equal("NEMOUR Kaylia", rows.First(r => r.Position == 1).Competitor);
    }

    [Fact]
    public void Rhythmic_group_events_are_national_teams_without_a_competitor()
    {
        var rows = _parser.Parse(Rhythmic, "World Cup / Group 5x").Entries;

        Assert.Equal(21, rows.Count);
        Assert.Equal("CHN", rows.First(r => r.Position == 1).ISO3);
        Assert.Null(rows.First(r => r.Position == 1).Competitor);
        Assert.Equal("RUS", rows.First(r => r.Position == 2).ISO3);
    }

    [Fact]
    public void Publishes_no_ranking_date()
    {
        Assert.Null(_parser.Parse(Rhythmic, "World Cup / Hoop").RankingDate);
    }

    [Theory]
    [MemberData(nameof(EveryTable))]
    public void Every_table_yields_three_letter_codes_and_positive_positions(string fixture, string selector)
    {
        var rows = _parser.Parse(Fixture.Read(fixture), selector).Entries;

        Assert.True(rows.Count >= 15, $"{fixture} {selector}: expected a full table, got {rows.Count} rows");
        Assert.All(rows, r =>
        {
            Assert.Matches("^[A-Z]{3}$", r.ISO3);
            Assert.True(r.Position > 0);
        });
    }

    public static TheoryData<string, string> EveryTable()
    {
        TheoryData<string, string> data = new();
        string[] series = ["World Cup", "World Challenge Cup"];
        var pages = new (string Fixture, string[] Apparatus)[]
        {
            ("Fig_Artistic_Men.html", ["Floor Exercise", "Pommel Horse", "Still Rings", "Vault", "Parallel Bars", "Horizontal Bar"]),
            ("Fig_Artistic_Women.html", ["Vault", "Uneven Bars", "Balance Beam", "Floor Exercise"]),
            ("Fig_Rhythmic_Women.html", ["Individual All-Around", "Hoop", "Ball", "Clubs", "Ribbon", "Group All-Around", "Group 5x", "Group 3x+2x"]),
        };

        foreach (var (fixture, apparatus) in pages)
        {
            foreach (string s in series)
            {
                foreach (string a in apparatus)
                {
                    data.Add(fixture, $"{s} / {a}");
                }
            }
        }

        return data;
    }

    [Theory]
    [InlineData("World Cup / Trampoline", "Trampoline")]
    [InlineData("Nations Cup / Vault", "Nations Cup")]
    [InlineData("Vault", "Vault")]
    public void Unknown_series_or_apparatus_is_a_parse_error_naming_the_value(string selector, string named)
    {
        var ex = Assert.Throws<ParseException>(() => _parser.Parse(Women, selector));

        Assert.Contains(named, ex.Message);
    }

    [Fact]
    public void A_missing_selector_is_a_parse_error()
    {
        Assert.Throws<ParseException>(() => _parser.Parse(Women));
    }

    [Fact]
    public void Carries_the_total_as_points()
    {
        Assert.Equal(71m, _parser.Parse(Men, "World Cup / Floor Exercise").Entries.First(r => r.Position == 1).Points);
    }
}
