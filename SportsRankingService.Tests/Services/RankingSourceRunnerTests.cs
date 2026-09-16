using Microsoft.Extensions.Logging.Abstractions;
using SportsRankingService.Models;
using SportsRankingService.Parsers;
using SportsRankingService.Parsing;
using SportsRankingService.Services;
using SportsRankingService.Services.UrlResolvers;

namespace SportsRankingService.Tests.Services;

/// <summary>
/// End-to-end for one configured item without the network: resolve the URL, fetch, parse, stamp.
/// </summary>
public class RankingSourceRunnerTests
{
    private sealed class FakeFetcher(Dictionary<string, string> pages) : IHttpFetcher
    {
        public List<string> Requested { get; } = [];

        public Task<string?> GetStringAsync(string url, CancellationToken cancellationToken)
        {
            Requested.Add(url);
            return Task.FromResult(pages.GetValueOrDefault(url));
        }
    }

    private static RankingSourceRunner Runner(FakeFetcher fetcher) =>
        new(fetcher,
            [new FihParser(), new FifaV3Parser()],
            [new IdentityUrlResolver(), new FifaDateIdResolver(fetcher)],
            NullLogger<RankingSourceRunner>.Instance);

    [Fact]
    public async Task Static_feed_is_fetched_parsed_and_stamped()
    {
        var fetcher = new FakeFetcher(new() { ["http://fih/outdoor_m.json"] = Fixture.Read("Fih_Outdoor_Men.json") });
        var item = new RankingItem { Sport = "Field Hockey", Event = "Outdoor", Gender = "Men", Url = "http://fih/outdoor_m.json", Source = "Fih" };

        var rows = await Runner(fetcher).RunAsync(item, CancellationToken.None);

        Assert.Equal(104, rows.Count);
        Assert.All(rows, r => Assert.Equal("Field Hockey", r.Sport));
        Assert.Equal("DEU", rows.Single(r => r.Position == 1).ISO3);
    }

    [Fact]
    public async Task Resolver_runs_its_preliminary_request_before_the_ranking_request()
    {
        var fetcher = new FakeFetcher(new()
        {
            ["https://inside.fifa.com/fifa-rankings/world-ranking/men"] = Fixture.Read("Fifa_WorldRanking_Men.html"),
            ["http://fifa/api?id=FRS_Male_Football_20260611"] = Fixture.Read("Fifa_V3_Men_FRS_20260611.json"),
        });
        var item = new RankingItem { Sport = "Soccer", Gender = "Men", Url = "http://fifa/api?id={0}", Source = "FifaV3", UrlResolver = "FifaDateId" };

        var rows = await Runner(fetcher).RunAsync(item, CancellationToken.None);

        Assert.Equal(211, rows.Count);
        Assert.Equal(["https://inside.fifa.com/fifa-rankings/world-ranking/men", "http://fifa/api?id=FRS_Male_Football_20260611"], fetcher.Requested);
    }

    [Fact]
    public async Task Failed_fetch_yields_no_rows_and_does_not_throw()
    {
        var fetcher = new FakeFetcher([]);
        var item = new RankingItem { Sport = "Field Hockey", Gender = "Men", Url = "http://down", Source = "Fih" };

        var rows = await Runner(fetcher).RunAsync(item, CancellationToken.None);

        Assert.Empty(rows);
    }

    [Fact]
    public async Task Unknown_source_is_a_configuration_error()
    {
        var item = new RankingItem { Sport = "Chess", Gender = "Men", Url = "http://x", Source = "Fide" };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Runner(new FakeFetcher([])).RunAsync(item, CancellationToken.None));

        Assert.Contains("Fide", ex.Message);
    }

    [Fact]
    public async Task Malformed_response_surfaces_as_a_parse_exception()
    {
        var fetcher = new FakeFetcher(new() { ["http://fih"] = "<html>maintenance</html>" });
        var item = new RankingItem { Sport = "Field Hockey", Gender = "Men", Url = "http://fih", Source = "Fih" };

        await Assert.ThrowsAsync<ParseException>(() => Runner(fetcher).RunAsync(item, CancellationToken.None));
    }
}
