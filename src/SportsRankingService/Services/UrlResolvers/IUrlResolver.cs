using SportsRankingService.Models;

namespace SportsRankingService.Services.UrlResolvers;

/// <summary>
/// Turns a configured <see cref="RankingItem"/> into the URL to fetch. Most feeds are static
/// (<see cref="IdentityUrlResolver"/>); some need a preliminary request to discover an id or date
/// that the ranking URL depends on. That request goes through the fetcher the runner hands in, the one the
/// item's <c>Fetcher</c> names, so a preliminary page on a host that refuses one client is read with the client
/// the feed itself uses (WBSC's release-date page sits behind the same CloudFront block as its feeds).
/// Registered as plain singletons; the runner indexes them by <see cref="Name"/>, the value of
/// <c>RankingItem.UrlResolver</c> in serviceconfig.json.
/// </summary>
public interface IUrlResolver
{
    string Name { get; }

    Task<ResolvedUrl> ResolveAsync(RankingItem item, IHttpFetcher fetcher, CancellationToken cancellationToken);
}
