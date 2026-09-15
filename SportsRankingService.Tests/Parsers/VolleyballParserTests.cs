using Microsoft.Extensions.Logging.Abstractions;
using SportsRankingService.Models;
using SportsRankingService.Parsers;

namespace SportsRankingService.Tests.Parsers;

public class VolleyballParserTests
{
    private readonly VolleyballParser _parser = new(NullLogger<VolleyballParser>.Instance);

    private static readonly RankingItem IndoorMen = new() { Sport = "Volleyball", Gender = "Men", Url = "" };

    [Fact]
    public void Parses_every_ranked_team()
    {
        var rows = _parser.ParseResponse(Fixture.Read("VolleyballWorld_Men.json"), IndoorMen).ToList();

        Assert.Equal(100, rows.Count);
        Assert.Equal("POL", rows.Single(r => r.Position == 1).ISO3);
        Assert.Equal("BIH", rows.Single(r => r.Position == 100).ISO3);
        Assert.All(rows, r => Assert.Matches("^[A-Z]{3}$", r.ISO3));
    }
}
