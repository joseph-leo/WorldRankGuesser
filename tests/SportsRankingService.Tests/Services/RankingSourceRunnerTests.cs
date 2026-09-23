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

    private sealed class FakeFetcher(Dictionary<string, string> pages, string name = HttpFetcher.FetcherName) : IHttpFetcher
    {
        public string Name => name;

        public List<string> Requested { get; } = [];

        public Task<string?> GetStringAsync(string url, CancellationToken cancellationToken)
        {
            Requested.Add(url);
            return Task.FromResult(pages.GetValueOrDefault(url));
        }
    }

    /// <summary>
    /// A parser for pages written as "&lt;date or nothing&gt;|&lt;position&gt;:&lt;ISO3&gt;,...", so a test can give one page a
    /// ranking date and another none, which no real parser of a paged feed can be made to do.
    /// </summary>
    private sealed class FakeParser : IRankingParser
    {
        public string SourceName => "Fake";

        public ParsedRanking Parse(string response, string? selector = null)
        {
            string[] parts = response.Split('|');
            List<RankEntry> entries = parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(e => e.Split(':'))
                .Select(e => new RankEntry(short.Parse(e[0]), e[1]))
                .ToList();
            return new ParsedRanking(entries, parts[0].Length == 0 ? null : DateOnly.Parse(parts[0]));
        }
    }

    private static RankingSourceRunner Runner(FakeFetcher fetcher, params FakeFetcher[] otherFetchers) =>
        new([fetcher, .. otherFetchers],
            [new FihParser(), new FifaV3Parser(), new FigParser(), new WtaParser(), new FakeParser()],
            [new IdentityUrlResolver(), new FifaDateIdResolver(fetcher)],
            new FakeTimeProvider(Now),
            NullLogger<RankingSourceRunner>.Instance);

    /// <summary>One page of the WTA players API: a top-level array, every entry carrying the same rankedAt.</summary>
    private static string WtaPage(string rankedAt, params (short Ranking, string Country, string Name)[] players) =>
        "[" + string.Join(",", players.Select(p =>
            $$"""{"player":{"countryCode":"{{p.Country}}","fullName":"{{p.Name}}"},"ranking":{{p.Ranking}},"points":10,"rankedAt":"{{rankedAt}}"}""")) + "]";

    private static RankingItem PagedWtaItem(int? firstPage = 0) =>
        new() { Sport = "Tennis", Event = "Doubles", Gender = "Women", Url = "http://wta?page={page}&pageSize=100", Source = "Wta", FirstPage = firstPage };

    // ---- Paged feeds: a URL with {page} is fetched page by page from FirstPage until a page yields no entries ----------------

    [Fact]
    public async Task A_paged_feed_fetches_pages_in_order_until_an_empty_one_and_concatenates_their_entries()
    {
        var fetcher = new FakeFetcher(new()
        {
            ["http://wta?page=0&pageSize=100"] = Fixture.Read("Wta_Doubles.json"),
            ["http://wta?page=1&pageSize=100"] = WtaPage("2026-09-14T00:00:00Z", (101, "USA", "Ann"), (102, "FRA", "Bea")),
            ["http://wta?page=2&pageSize=100"] = "[]",
        });

        RankingSnapshot? snapshot = await Runner(fetcher).RunAsync(PagedWtaItem(), CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal(["http://wta?page=0&pageSize=100", "http://wta?page=1&pageSize=100", "http://wta?page=2&pageSize=100"], fetcher.Requested);
        Assert.Equal(102, snapshot.Entries.Count);
        Assert.Equal("FRA", snapshot.Entries.Single(e => e.Position == 102).ISO3);
        Assert.Equal(new DateOnly(2026, 9, 14), snapshot.RankingDate);
        Assert.True(snapshot.IsFederationDate);
    }

    /// <summary>A list with a hole would be stored as a new, shorter release; keeping the previous release is better.</summary>
    [Fact]
    public async Task A_page_that_cannot_be_fetched_fails_the_whole_feed()
    {
        var fetcher = new FakeFetcher(new()
        {
            ["http://wta?page=0&pageSize=100"] = Fixture.Read("Wta_Doubles.json"),
            // page 1 is missing
            ["http://wta?page=2&pageSize=100"] = WtaPage("2026-09-14T00:00:00Z", (201, "USA", "Cat")),
        });

        RankingSnapshot? snapshot = await Runner(fetcher).RunAsync(PagedWtaItem(), CancellationToken.None);

        Assert.Null(snapshot);
        Assert.Equal(["http://wta?page=0&pageSize=100", "http://wta?page=1&pageSize=100"], fetcher.Requested);
    }

    [Fact]
    public async Task A_page_with_a_different_ranking_date_fails_the_parse_because_the_list_changed_between_pages()
    {
        var fetcher = new FakeFetcher(new()
        {
            ["http://wta?page=0&pageSize=100"] = Fixture.Read("Wta_Doubles.json"),
            ["http://wta?page=1&pageSize=100"] = WtaPage("2026-09-21T00:00:00Z", (101, "USA", "Ann")),
            ["http://wta?page=2&pageSize=100"] = "[]",
        });

        var ex = await Assert.ThrowsAsync<ParseException>(() => Runner(fetcher).RunAsync(PagedWtaItem(), CancellationToken.None));

        Assert.Contains("2026-09-21", ex.Message);
        Assert.Contains("2026-09-14", ex.Message);
    }

    /// <summary>An API that ignores the page parameter would answer the first page forever.</summary>
    [Fact]
    public async Task A_feed_still_yielding_entries_after_100_pages_fails_the_parse()
    {
        var pages = new Dictionary<string, string>();
        for (int page = 0; page <= 100; page++)
        {
            pages[$"http://wta?page={page}&pageSize=100"] = WtaPage("2026-09-14T00:00:00Z", ((short)(page + 1), "USA", $"Player {page}"));
        }
        var fetcher = new FakeFetcher(pages);

        var ex = await Assert.ThrowsAsync<ParseException>(() => Runner(fetcher).RunAsync(PagedWtaItem(), CancellationToken.None));

        Assert.Contains("100", ex.Message);
        Assert.Equal(100, fetcher.Requested.Count);
    }

    /// <summary>The rule is symmetric: the first page sets the date, dated or not, and every later page must agree.</summary>
    [Fact]
    public async Task A_dated_page_after_a_dateless_first_page_fails_the_parse_too()
    {
        var fetcher = new FakeFetcher(new()
        {
            ["http://fake?page=1"] = "|1:USA,2:FRA",
            ["http://fake?page=2"] = "2026-09-14|3:DEU",
            ["http://fake?page=3"] = "|",
        });
        var item = new RankingItem { Sport = "Chess", Gender = "Men", Url = "http://fake?page={page}", Source = "Fake" };

        var ex = await Assert.ThrowsAsync<ParseException>(() => Runner(fetcher).RunAsync(item, CancellationToken.None));

        Assert.Contains("2026-09-14", ex.Message);
    }

    [Fact]
    public async Task A_paged_url_without_a_first_page_counts_from_1()
    {
        var fetcher = new FakeFetcher(new()
        {
            ["http://wta?page=1&pageSize=100"] = WtaPage("2026-09-14T00:00:00Z", (1, "USA", "Ann")),
            ["http://wta?page=2&pageSize=100"] = "[]",
        });

        RankingSnapshot? snapshot = await Runner(fetcher).RunAsync(PagedWtaItem(firstPage: null), CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal(["http://wta?page=1&pageSize=100", "http://wta?page=2&pageSize=100"], fetcher.Requested);
        Assert.Single(snapshot.Entries);
    }

    /// <summary>An empty snapshot is what the updater refuses to save (RankingUpdaterTests), so an empty list stores no release.</summary>
    [Fact]
    public async Task An_empty_first_page_yields_a_snapshot_with_no_entries()
    {
        var fetcher = new FakeFetcher(new() { ["http://wta?page=0&pageSize=100"] = "[]" });

        RankingSnapshot? snapshot = await Runner(fetcher).RunAsync(PagedWtaItem(), CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Empty(snapshot.Entries);
        Assert.Equal(["http://wta?page=0&pageSize=100"], fetcher.Requested);
    }

    /// <summary>The resolvers that discover an id or date string.Format the URL, where {page} is a format error, not a page.</summary>
    [Fact]
    public async Task A_paged_url_with_a_formatting_resolver_is_a_configuration_error()
    {
        var fetcher = new FakeFetcher([]);
        var item = new RankingItem { Sport = "Soccer", Gender = "Men", Url = "http://fifa/api?id={0}&page={page}", Source = "FifaV3", UrlResolver = "FifaDateId", FirstPage = 1 };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Runner(fetcher).RunAsync(item, CancellationToken.None));

        Assert.Contains("FifaDateId", ex.Message);
        Assert.Contains("{page}", ex.Message);
        Assert.Empty(fetcher.Requested);
    }

    [Fact]
    public async Task A_first_page_on_a_url_without_the_placeholder_is_a_configuration_error()
    {
        var fetcher = new FakeFetcher(new() { ["http://wta?page=0"] = Fixture.Read("Wta_Doubles.json") });
        var item = new RankingItem { Sport = "Tennis", Event = "Doubles", Gender = "Women", Url = "http://wta?page=0", Source = "Wta", FirstPage = 0 };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Runner(fetcher).RunAsync(item, CancellationToken.None));

        Assert.Contains("{page}", ex.Message);
        Assert.Contains("Tennis Doubles Women", ex.Message);
        Assert.Empty(fetcher.Requested);
    }

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

    /// <summary>The updater runs the feeds in parallel; behind the caching fetcher two tables of one page cost one request.</summary>
    [Fact]
    public async Task Feeds_sharing_a_page_fetch_it_once_and_each_parse_their_own_table()
    {
        var pages = new FakeFetcher(new() { ["http://fig/rg"] = Fixture.Read("Fig_Rhythmic_Women.html") });
        var runner = new RankingSourceRunner(
            [new CachingFetcher(pages)],
            [new FigParser()],
            [new IdentityUrlResolver()],
            new FakeTimeProvider(Now),
            NullLogger<RankingSourceRunner>.Instance);
        var group = new RankingItem { Sport = "Rhythmic Gymnastics", Event = "World Cup Group 5x", Gender = "Women", Url = "http://fig/rg", Source = "Fig", Selector = "World Cup / Group 5x" };
        var hoop = new RankingItem { Sport = "Rhythmic Gymnastics", Event = "World Cup Hoop", Gender = "Women", Url = "http://fig/rg", Source = "Fig", Selector = "World Cup / Hoop" };

        RankingSnapshot?[] snapshots = await Task.WhenAll(
            runner.RunAsync(group, CancellationToken.None),
            runner.RunAsync(hoop, CancellationToken.None));

        Assert.Equal(["http://fig/rg"], pages.Requested);
        Assert.Equal(21, snapshots[0]!.Entries.Count);
        Assert.Equal("World Cup Hoop", snapshots[1]!.Event);
        Assert.NotEmpty(snapshots[1]!.Entries);
        Assert.NotEqual(snapshots[0]!.Entries, snapshots[1]!.Entries);
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
    public async Task An_item_is_fetched_through_the_fetcher_it_names()
    {
        var http = new FakeFetcher([]);
        var curl = new FakeFetcher(new() { ["http://fih/outdoor_m.json"] = Fixture.Read("Fih_Outdoor_Men.json") }, name: "Curl");
        var item = new RankingItem { Sport = "Field Hockey", Event = "Outdoor", Gender = "Men", Url = "http://fih/outdoor_m.json", Source = "Fih", Fetcher = "curl" };

        RankingSnapshot? snapshot = await Runner(http, curl).RunAsync(item, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal(["http://fih/outdoor_m.json"], curl.Requested);
        Assert.Empty(http.Requested);
    }

    [Fact]
    public async Task Unknown_fetcher_is_a_configuration_error()
    {
        var item = new RankingItem { Sport = "Field Hockey", Gender = "Men", Url = "http://x", Source = "Fih", Fetcher = "Wget" };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Runner(new FakeFetcher([])).RunAsync(item, CancellationToken.None));

        Assert.Contains("Wget", ex.Message);
    }

    [Fact]
    public async Task Malformed_response_surfaces_as_a_parse_exception()
    {
        var fetcher = new FakeFetcher(new() { ["http://fih"] = "<html>maintenance</html>" });
        var item = new RankingItem { Sport = "Field Hockey", Gender = "Men", Url = "http://fih", Source = "Fih" };

        await Assert.ThrowsAsync<ParseException>(() => Runner(fetcher).RunAsync(item, CancellationToken.None));
    }
}
