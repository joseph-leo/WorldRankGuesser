using SportsRankingService.Models;
using SportsRankingService.Parsing;
using SportsRankingService.Services.UrlResolvers;

namespace SportsRankingService.Services;

/// <summary>
/// Runs one configured feed end to end: resolve the URL, fetch it, parse it, build the snapshot.
/// A failed fetch yields null (already logged by the fetcher). A malformed response throws
/// <see cref="ParseException"/>; an unknown Source, UrlResolver or Fetcher name throws
/// <see cref="InvalidOperationException"/> because that is a configuration error.
/// </summary>
public sealed class RankingSourceRunner : IRankingSourceRunner
{
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

        ResolvedUrl resolved = await resolver.ResolveAsync(item, cancellationToken);
        string? response = await fetcher.GetStringAsync(resolved.Url, cancellationToken);

        if (response is null)
        {
            return null;
        }

        ParsedRanking parsed = parser.Parse(response, item.Selector);
        DateOnly today = DateOnly.FromDateTime(_clock.GetLocalNow().Date);
        RankingSnapshot snapshot = RankingSnapshotBuilder.Build(item, parsed, resolved.RankingDate, today);

        _logger.LogInformation("Parsed {Count} rows for {Item}, ranking date {Date} ({DateSource})",
            snapshot.Entries.Count, snapshot.Describe(), snapshot.RankingDate, snapshot.IsFederationDate ? "federation" : "scrape date");

        return snapshot;
    }
}
