using System.Globalization;
using SportsRankingService.Models;
using SportsRankingService.Parsing;
using SportsRankingService.Services.UrlResolvers;

namespace SportsRankingService.Services;

/// <summary>
/// Runs one configured feed end to end: resolve the URL, fetch it (page by page when the URL carries {page}),
/// parse it, build the snapshot. A failed fetch yields null (already logged by the fetcher). A malformed response
/// throws <see cref="ParseException"/>; an unknown Source, UrlResolver or Fetcher name throws
/// <see cref="InvalidOperationException"/> because that is a configuration error.
/// </summary>
public sealed class RankingSourceRunner : IRankingSourceRunner
{
    /// <summary>In a <see cref="RankingItem.Url"/>: replaced by the page number, from <see cref="RankingItem.FirstPage"/> up.</summary>
    public const string PagePlaceholder = "{page}";

    /// <summary>More pages than any feed has (WTA doubles: 19 of 100); reaching it means the API ignores the page number.</summary>
    private const int MaxPages = 100;

    private readonly IReadOnlyDictionary<string, IHttpFetcher> _fetchers;
    private readonly IReadOnlyDictionary<string, IRankingParser> _parsers;
    private readonly IReadOnlyDictionary<string, IUrlResolver> _resolvers;
    private readonly TimeProvider _clock;
    private readonly ILogger<RankingSourceRunner> _logger;

    public RankingSourceRunner(
        IEnumerable<IHttpFetcher> fetchers,
        IEnumerable<IRankingParser> parsers,
        IEnumerable<IUrlResolver> resolvers,
        TimeProvider clock,
        ILogger<RankingSourceRunner> logger)
    {
        _fetchers = fetchers.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
        _parsers = parsers.ToDictionary(p => p.SourceName, StringComparer.OrdinalIgnoreCase);
        _resolvers = resolvers.ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);
        _clock = clock;
        _logger = logger;
    }

    public async Task<RankingSnapshot?> RunAsync(RankingItem item, CancellationToken cancellationToken)
    {
        if (!_parsers.TryGetValue(item.Source, out IRankingParser? parser))
        {
            throw new InvalidOperationException($"No parser registered for Source '{item.Source}' ({item.Describe()}). Known: {string.Join(", ", _parsers.Keys)}");
        }

        if (!_resolvers.TryGetValue(item.UrlResolver, out IUrlResolver? resolver))
        {
            throw new InvalidOperationException($"No URL resolver registered for '{item.UrlResolver}' ({item.Describe()}). Known: {string.Join(", ", _resolvers.Keys)}");
        }

        if (!_fetchers.TryGetValue(item.Fetcher, out IHttpFetcher? fetcher))
        {
            throw new InvalidOperationException($"No fetcher registered for '{item.Fetcher}' ({item.Describe()}). Known: {string.Join(", ", _fetchers.Keys)}");
        }

        if (item.FirstPage is not null && !item.Url.Contains(PagePlaceholder, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"FirstPage is set but the Url has no {PagePlaceholder} placeholder ({item.Describe()})");
        }

        // The resolvers that discover an id or date string.Format the Url, where {page} is a format error, not a page.
        if (item.Url.Contains(PagePlaceholder, StringComparison.Ordinal) && !string.Equals(item.UrlResolver, IdentityUrlResolver.ResolverName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"A Url with {PagePlaceholder} cannot use the '{item.UrlResolver}' resolver, which formats the Url ({item.Describe()})");
        }

        ResolvedUrl resolved = await resolver.ResolveAsync(item, cancellationToken);
        ParsedRanking? parsed = resolved.Url.Contains(PagePlaceholder, StringComparison.Ordinal)
            ? await ParsePagesAsync(item, resolved.Url, fetcher, parser, cancellationToken)
            : await ParseOneAsync(item, resolved.Url, fetcher, parser, cancellationToken);

        if (parsed is null)
        {
            return null;
        }

        DateOnly today = DateOnly.FromDateTime(_clock.GetLocalNow().Date);
        RankingSnapshot snapshot = RankingSnapshotBuilder.Build(item, parsed, resolved.RankingDate, today);

        _logger.LogInformation("Parsed {Count} rows for {Item}, ranking date {Date} ({DateSource})",
            snapshot.Entries.Count, snapshot.Describe(), snapshot.RankingDate, snapshot.IsFederationDate ? "federation" : "scrape date");

        return snapshot;
    }

    private static string Describe(DateOnly? rankingDate) =>
        rankingDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "none";

    private static async Task<ParsedRanking?> ParseOneAsync(RankingItem item, string url, IHttpFetcher fetcher, IRankingParser parser, CancellationToken cancellationToken)
    {
        string? response = await fetcher.GetStringAsync(url, cancellationToken);
        return response is null ? null : parser.Parse(response, item.Selector);
    }

    /// <summary>
    /// A paged list is fetched from <see cref="RankingItem.FirstPage"/> up, each page parsed on its own, until a page
    /// yields no entries (that page is dropped). A page that cannot be fetched fails the whole feed, like any other
    /// fetch failure: a list with a hole would be stored as a new, shorter release, which is worse than keeping the
    /// previous one. The ranking date is the first page's.
    /// </summary>
    private async Task<ParsedRanking?> ParsePagesAsync(RankingItem item, string urlTemplate, IHttpFetcher fetcher, IRankingParser parser, CancellationToken cancellationToken)
    {
        List<RankEntry> entries = [];
        DateOnly? rankingDate = null;
        int firstPage = item.FirstPage ?? 1;

        for (int page = firstPage; ; page++)
        {
            bool first = page == firstPage;

            // An API that ignores the page parameter answers its first page forever.
            if (page - firstPage >= MaxPages)
            {
                throw new ParseException(item.Source, $"still yielding entries after {MaxPages} pages: does the API honour {PagePlaceholder}?");
            }

            string url = urlTemplate.Replace(PagePlaceholder, page.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            string? response = await fetcher.GetStringAsync(url, cancellationToken);

            if (response is null)
            {
                _logger.LogWarning("Page {Page} of {Item} could not be fetched; the feed keeps its previous release", page, item.Describe());
                return null;
            }

            ParsedRanking parsed = parser.Parse(response, item.Selector);
            if (parsed.Entries.Count == 0)
            {
                return new ParsedRanking(entries, rankingDate);
            }

            // The first page sets the date, dated or not, and every later page must agree: a new ranking published
            // between two page requests would give a list that is half old and half new.
            if (first)
            {
                rankingDate = parsed.RankingDate;
            }
            else if (parsed.RankingDate != rankingDate)
            {
                throw new ParseException(item.Source, $"page {page} carries ranking date {Describe(parsed.RankingDate)} but the first page {Describe(rankingDate)}: the list changed between pages");
            }

            entries.AddRange(parsed.Entries);
        }
    }
}
