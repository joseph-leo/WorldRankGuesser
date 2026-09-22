using System.Collections.Concurrent;

namespace SportsRankingService.Services;

/// <summary>
/// Requests each URL from the wrapped fetcher once and answers every later or concurrent request
/// with that same response. Several feeds read one page (one FIG page holds every apparatus table,
/// the Wikipedia IIHF article both sexes) and several resolvers one preliminary page (WBSC, SVNS),
/// and the feeds run in parallel, so the requests usually overlap: the task is what is shared, not
/// only its result. A failed fetch (null) is shared as well, so eight feeds on a dead page fail
/// from one request. The cache lives as long as this singleton, which in a run-once process is one
/// run; a long-lived host would need a cache per run. The shared request runs on the first
/// caller's cancellation token, which is the one token <c>UpdateAllAsync</c> gives every feed.
/// </summary>
public sealed class CachingFetcher(IHttpFetcher inner) : IHttpFetcher
{
    // Lazy because GetOrAdd may run its factory for two racing callers; only one Lazy is kept and only it is started.
    private readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _responses = new(StringComparer.Ordinal);

    public string Name => inner.Name;

    public Task<string?> GetStringAsync(string url, CancellationToken cancellationToken) =>
        _responses.GetOrAdd(url, u => new Lazy<Task<string?>>(() => inner.GetStringAsync(u, cancellationToken))).Value;
}
