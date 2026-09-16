using SportsRankingService.Parsers;

namespace SportsRankingService.Tests.Parsers;

/// <summary>
/// FIBA's ranking page (2025/26 design): columns are "#, country, zone rank, PTS, +/-" with no
/// IOC code, the country name is localized, and the only stable country identifier is the
/// English slug in the team link (e.g. /fr/teams/154-usa).
/// </summary>
public class FibaParserTests
{
    private readonly FibaParser _parser = new();

    [Fact]
    public void Parses_country_from_the_team_link_slug()
    {
        var rows = _parser.Parse(Fixture.Read("Fiba_Ranking_Men.html"));

        Assert.Equal("USA", rows.First(r => r.Position == 1).ISO3);
        Assert.Equal("DEU", rows.First(r => r.Position == 2).ISO3);
        Assert.Equal("FRA", rows.First(r => r.Position == 3).ISO3);
    }

    [Fact]
    public void Every_row_has_a_three_letter_code_and_a_positive_position()
    {
        var rows = _parser.Parse(Fixture.Read("Fiba_Ranking_Men.html"));

        Assert.True(rows.Count >= 150, $"expected the full ranking, got {rows.Count} rows");
        Assert.All(rows, r =>
        {
            Assert.Matches("^[A-Z]{3}$", r.ISO3);
            Assert.True(r.Position > 0);
        });
    }
}
