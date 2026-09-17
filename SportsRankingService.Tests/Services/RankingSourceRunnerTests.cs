using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using SportsRankingService.Models;
using SportsRankingService.Parsers;
using SportsRankingService.Parsing;
using SportsRankingService.Services;
using SportsRankingService.Services.UrlResolvers;

namespace SportsRankingService.Tests.Services;

/// <summary>
/// End-to-end for one configured item without the network: resolve the URL, fetch, parse, stamp, date.
/// </summary>
public class RankingSourceRunnerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

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
            [new FihParser(), new FifaV3Parser(), new FigParser()],
            [new IdentityUrlResolver(), new FifaDateIdResolver(fetcher)],
            new FakeTimeProvider(Now),
            NullLogger<RankingSourceRunner>.Instance);

    [Fact]
    public async Task Static_feed_is_fetched_parsed_and_stamped()
    {
        var fetcher = new FakeFetcher(new() { ["http://fih/outdoor_m.json"] = Fixture.Read("Fih_Outdoor_Men.json") });
        var item = new RankingItem { Sport = "Field Hockey", Event = "Outdoor", Gender = "Men", Url = "http://fih/outdoor_m.json", Source = "Fih" };

        RankingSnapshot? snapshot = await Runner(fetcher).RunAsync(item, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal("Field Hockey", snapshot.Sport);
        Assert.Equal("Outdoor", snapshot.Event);
        Assert.Equal("Men", snapshot.Gender);
        Assert.Equal(104, snapshot.Entries.Count);
        Assert.Equal("DEU", snapshot.Entries.Single(e => e.Position == 1).ISO3);
    }

    [Fact]
    public async Task A_dateless_feed_gets_todays_date_flagged_as_not_the_federations()
    {
        var fetcher = new FakeFetcher(new() { ["http://fih/outdoor_m.json"] = Fixture.Read("Fih_Outdoor_Men.json") });
        var item = new RankingItem { Sport = "Field Hockey", Event = "Outdoor", Gender = "Men", Url = "http://fih/outdoor_m.json", Source = "Fih" };

        RankingSnapshot? snapshot = await Runner(fetcher).RunAsync(item, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal(new DateOnly(2026, 9, 15), snapshot.RankingDate);
        Assert.False(snapshot.IsFederationDate);
    }

    [Fact]
    public async Task Resolver_runs_its_preliminary_request_and_supplies_the_ranking_date()
    {
        var fetcher = new FakeFetcher(new()
        {
            ["https://inside.fifa.com/fifa-rankings/world-ranking/men"] = Fixture.Read("Fifa_WorldRanking_Men.html"),
            ["http://fifa/api?id=FRS_Male_Football_20260611"] = Fixture.Read("Fifa_V3_Men_FRS_20260611.json"),
        });
        var item = new RankingItem { Sport = "Soccer", Gender = "Men", Url = "http://fifa/api?id={0}", Source = "FifaV3", UrlResolver = "FifaDateId" };

        RankingSnapshot? snapshot = await Runner(fetcher).RunAsync(item, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal(211, snapshot.Entries.Count);
        Assert.Equal(new DateOnly(2026, 7, 20), snapshot.RankingDate);
        Assert.True(snapshot.IsFederationDate);
        Assert.Equal(["https://inside.fifa.com/fifa-rankings/world-ranking/men", "http://fifa/api?id=FRS_Male_Football_20260611"], fetcher.Requested);
    }

    [Fact]
    public async Task Selector_reaches_the_parser_for_a_page_holding_several_tables()
    {
        var fetcher = new FakeFetcher(new() { ["http://fig/rg"] = Fixture.Read("Fig_Rhythmic_Women.html") });
        var item = new RankingItem { Sport = "Rhythmic Gymnastics", Event = "World Cup Group 5x", Gender = "Women", Url = "http://fig/rg", Source = "Fig", Selector = "World Cup / Group 5x" };

        RankingSnapshot? snapshot = await Runner(fetcher).RunAsync(item, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal("World Cup Group 5x", snapshot.Event);
        Assert.Equal(21, snapshot.Entries.Count);
        Assert.Equal("CHN", snapshot.Entries.Single(e => e.Position == 1).ISO3);
    }

    [Fact]
    public async Task Failed_fetch_yields_null_and_does_not_throw()
    {
        var fetcher = new FakeFetcher([]);
        var item = new RankingItem { Sport = "Field Hockey", Gender = "Men", Url = "http://down", Source = "Fih" };

        RankingSnapshot? snapshot = await Runner(fetcher).RunAsync(item, CancellationToken.None);

        Assert.Null(snapshot);
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
