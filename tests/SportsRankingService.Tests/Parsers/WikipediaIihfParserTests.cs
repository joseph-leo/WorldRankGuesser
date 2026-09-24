using SportsRankingService.Parsers;
using SportsRankingService.Parsing;

namespace SportsRankingService.Tests.Parsers;

/// <summary>
/// Wikipedia's "IIHF World Ranking" article stands in for iihf.com, which challenges every scripted client.
/// One page holds the men's and the women's table, each under its own h2; the selector names the table.
/// Columns are "current rank, previous rank, team, ..., current total (bold), ...". Teams the IIHF lists
/// without a rank (suspended or inactive) show "NR" and are not entries.
/// </summary>
public class WikipediaIihfParserTests
{
    private const string Page = "Wikipedia_IihfWorldRanking.html";

    private readonly WikipediaIihfParser _parser = new();

    [Fact]
    public void Men_selector_reads_the_mens_table()
    {
        var rows = _parser.Parse(Sample.Read(Page), "Men").Entries;

        Assert.Equal(12, rows.Count);
        Assert.Equal(new RankEntry(1, "CHE", Points: 5335m), rows.Single(r => r.Position == 1));
        Assert.Equal(new RankEntry(2, "CAN", Points: 5305m), rows.Single(r => r.Position == 2));
        Assert.Equal(new RankEntry(12, "ARM", Points: 805m), rows.Single(r => r.Position == 12));
    }

    [Fact]
    public void Women_selector_reads_the_womens_table()
    {
        var rows = _parser.Parse(Sample.Read(Page), "Women").Entries;

        Assert.Equal(10, rows.Count);
        Assert.Equal(new RankEntry(1, "USA", Points: 5460m), rows.Single(r => r.Position == 1));
        Assert.Equal(new RankEntry(3, "CZE", Points: 5125m), rows.Single(r => r.Position == 3));
        Assert.Equal(new RankEntry(10, "SGP", Points: 1135m), rows.Single(r => r.Position == 10));
    }

    [Fact]
    public void Teams_listed_as_not_ranked_are_not_entries()
    {
        var men = _parser.Parse(Sample.Read(Page), "Men").Entries;
        var women = _parser.Parse(Sample.Read(Page), "Women").Entries;

        Assert.DoesNotContain(men, r => r.ISO3 is "RUS" or "IND" or "MAR");     // "NR"
        Assert.DoesNotContain(women, r => r.ISO3 == "PHL");                     // "new"
    }

    [Fact]
    public void The_article_states_no_ranking_date()
    {
        Assert.Null(_parser.Parse(Sample.Read(Page), "Men").RankingDate);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Mixed")]
    public void A_selector_that_names_no_table_is_a_parse_error(string? selector)
    {
        var ex = Assert.Throws<ParseException>(() => _parser.Parse(Sample.Read(Page), selector));

        Assert.Contains("Men", ex.Message);
    }
}
