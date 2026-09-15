using Microsoft.Extensions.Logging.Abstractions;
using SportsRankingService.Models;
using SportsRankingService.Parsers;

namespace SportsRankingService.Tests.Parsers;

public class HockeyParserTests
{
    private readonly HockeyParser _parser = new(NullLogger<HockeyParser>.Instance);

    private static readonly RankingItem OutdoorMen = new() { Sport = "Field Hockey", Event = "Outdoor", Gender = "Men", Url = "" };

    [Fact]
    public void FieldHockey_parses_every_ranked_team()
    {
        var rows = _parser.ParseResponse(Fixture.Read("Fih_Outdoor_Men.json"), OutdoorMen).ToList();

        Assert.Equal(104, rows.Count);
        Assert.Equal("DEU", rows.Single(r => r.Position == 1).ISO3);
        Assert.Equal("MAR", rows.Single(r => r.Position == 104).ISO3);
    }

    [Fact]
    public void FieldHockey_stamps_sport_event_and_gender_from_the_ranking_item()
    {
        var rows = _parser.ParseResponse(Fixture.Read("Fih_Outdoor_Men.json"), OutdoorMen).ToList();

        Assert.All(rows, r =>
        {
            Assert.Equal("Field Hockey", r.Sport);
            Assert.Equal("Outdoor", ((SportsRanking)r).Event);
            Assert.Equal("Men", r.Gender);
        });
    }

    [Fact]
    public void FieldHockey_converts_IOC_codes_to_ISO3()
    {
        var rows = _parser.ParseResponse(Fixture.Read("Fih_Outdoor_Men.json"), OutdoorMen).ToList();

        Assert.All(rows, r => Assert.Matches("^[A-Z]{3}$", r.ISO3));
        Assert.DoesNotContain(rows, r => r.ISO3 == "GER");
    }

    [Fact(Skip = "No fixture: https://www.iihf.com/en/worldranking returns 403 to a scripted client (captured 2026-09-15). Needs a new source.")]
    public void IceHockey_parses_mens_and_womens_tables()
    {
    }
}
