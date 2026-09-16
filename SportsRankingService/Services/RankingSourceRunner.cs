using SportsRankingService.Models;
using SportsRankingService.Parsing;
using SportsRankingService.Services.UrlResolvers;

namespace SportsRankingService.Services;

/// <summary>
/// Runs one configured feed end to end: resolve the URL, fetch it, parse it, stamp the rows.
/// A failed fetch yields no rows (already logged by the fetcher). A malformed response throws
/// <see cref="ParseException"/>; an unknown Source or UrlResolver name throws
/// <see cref="InvalidOperationException"/> because that is a configuration error.
/// </summary>
public sealed class RankingSourceRunner
{
    private readonly IHttpFetcher _fetcher;
    private readonly IReadOnlyDictionary<string, IRankingParser> _parsers;
    private readonly IReadOnlyDictionary<string, IUrlResolver> _resolvers;
    private readonly ILogger<RankingSourceRunner> _logger;

    public RankingSourceRunner(
        IHttpFetcher fetcher,
        IEnumerable<IRankingParser> parsers,
        IEnumerable<IUrlResolver> resolvers,
        ILogger<RankingSourceRunner> logger)
    {
        _fetcher = fetcher;
        _parsers = parsers.ToDictionary(p => p.SourceName, StringComparer.OrdinalIgnoreCase);
        _resolvers = resolvers.ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    public async Task<IReadOnlyList<SportsRanking>> RunAsync(RankingItem item, CancellationToken cancellationToken)
    {
        if (!_parsers.TryGetValue(item.Source, out IRankingParser? parser))
        {
            throw new InvalidOperationException($"No parser registered for Source '{item.Source}' ({Describe(item)}). Known: {string.Join(", ", _parsers.Keys)}");
        }

        if (!_resolvers.TryGetValue(item.UrlResolver, out IUrlResolver? resolver))
        {
            throw new InvalidOperationException($"No URL resolver registered for '{item.UrlResolver}' ({Describe(item)}). Known: {string.Join(", ", _resolvers.Keys)}");
        }

        string url = await resolver.ResolveAsync(item, cancellationToken);
        string? response = await _fetcher.GetStringAsync(url, cancellationToken);

        if (response is null)
        {
            return [];
        }

        IReadOnlyList<RankEntry> entries = parser.Parse(response);
        List<SportsRanking> rows = RankingMapper.ToSportsRankings(entries, item);

        _logger.LogInformation("Parsed {Count} rows for {Item}", rows.Count, Describe(item));

        return rows;
    }

    internal static string Describe(RankingItem item) =>
        string.Join(" ", new[] { item.Sport, item.Event, item.Gender }.Where(s => !string.IsNullOrEmpty(s)));
}
