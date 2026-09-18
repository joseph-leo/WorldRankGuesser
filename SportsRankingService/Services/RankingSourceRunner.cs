using SportsRankingService.Models;
using SportsRankingService.Parsing;
using SportsRankingService.Services.UrlResolvers;

namespace SportsRankingService.Services;

/// <summary>
/// Runs one configured feed end to end: resolve the URL, fetch it, parse it, build the snapshot.
/// A failed fetch yields null (already logged by the fetcher). A malformed response throws
/// <see cref="ParseException"/>; an unknown Source or UrlResolver name throws
/// <see cref="InvalidOperationException"/> because that is a configuration error.
/// </summary>
public sealed class RankingSourceRunner : IRankingSourceRunner
{
    private readonly IHttpFetcher _fetcher;
    private readonly IReadOnlyDictionary<string, IRankingParser> _parsers;
    private readonly IReadOnlyDictionary<string, IUrlResolver> _resolvers;
    private readonly TimeProvider _clock;
    private readonly ILogger<RankingSourceRunner> _logger;

    public RankingSourceRunner(
        IHttpFetcher fetcher,
        IEnumerable<IRankingParser> parsers,
        IEnumerable<IUrlResolver> resolvers,
        TimeProvider clock,
        ILogger<RankingSourceRunner> logger)
    {
        _fetcher = fetcher;
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

        ResolvedUrl resolved = await resolver.ResolveAsync(item, cancellationToken);
        string? response = await _fetcher.GetStringAsync(resolved.Url, cancellationToken);

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
