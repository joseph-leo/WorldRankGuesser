using Microsoft.Extensions.Logging.Abstractions;
using SportsRankingService.Models;
using SportsRankingService.Parsers;

namespace SportsRankingService.Tests.Parsers;

public class SoccerParserTests
{
    private readonly SoccerParser _parser = new(NullLogger<SoccerParser>.Instance);

    private static readonly RankingItem Men = new() { Sport = "Soccer/Football", Gender = "Men", Url = "" };

    [Fact]
    public void Parses_ranked_teams_and_stops_at_the_first_unranked_one()
    {
        // The fixture holds 211 entries; the last has a null rank.
        var rows = _parser.ParseResponse(Fixture.Read("Fifa_Overview_Men_id14870.json"), Men).ToList();

        Assert.Equal(210, rows.Count);
        Assert.Equal("ESP", rows.Single(r => r.Position == 1).ISO3);
        Assert.Equal("SMR", rows.Single(r => r.Position == 210).ISO3);
    }

    [Fact]
    public void FIFA_codes_are_three_letters_but_include_non_ISO_home_nations()
    {
        var codes = _parser.ParseResponse(Fixture.Read("Fifa_Overview_Men_id14870.json"), Men).Select(r => r.ISO3).ToList();

        Assert.All(codes, c => Assert.Matches("^[A-Z]{3}$", c));
        Assert.Contains("ENG", codes);   // documented: not an ISO3 code, kept as FIFA emits it
    }
}
