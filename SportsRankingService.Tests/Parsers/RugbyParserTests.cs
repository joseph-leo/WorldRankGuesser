using Microsoft.Extensions.Logging.Abstractions;
using SportsRankingService.Models;
using SportsRankingService.Parsers;

namespace SportsRankingService.Tests.Parsers;

public class RugbyParserTests
{
    private readonly RugbyParser _parser = new(NullLogger<RugbyParser>.Instance);

    private static readonly RankingItem UnionMen = new() { Sport = "Rugby", Event = "Union", Gender = "Men", Url = "" };

    [Fact]
    public void Union_parses_every_ranked_team()
    {
        var rows = _parser.ParseResponse(Fixture.Read("WorldRugby_Union_Men.json"), UnionMen).ToList();

        Assert.Equal(114, rows.Count);
        Assert.Equal("ZAF", rows.Single(r => r.Position == 1).ISO3);
        Assert.Equal("ASM", rows.Single(r => r.Position == 114).ISO3);
        Assert.All(rows, r => Assert.Matches("^[A-Z]{3}$", r.ISO3));
    }

    [Fact(Skip = "No fixture: https://www.svns.com/en/standings is rendered client-side and the HTML holds no ranking data (captured 2026-09-15). Needs the underlying API.")]
    public void Sevens_parses_womens_and_mens_tables()
    {
    }
}
