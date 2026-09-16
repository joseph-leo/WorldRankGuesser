using SportsRankingService.Models;

namespace SportsRankingService.Services.UrlResolvers;

/// <summary>
/// Turns a configured <see cref="RankingItem"/> into the URL to fetch. Most feeds are static
/// (<see cref="IdentityUrlResolver"/>); some need a preliminary request to discover an id or date
/// that the ranking URL depends on. Registered as keyed services under <see cref="Name"/>, which is
/// the value of <c>RankingItem.UrlResolver</c> in serviceconfig.json.
/// </summary>
public interface IUrlResolver
{
    string Name { get; }

    Task<ResolvedUrl> ResolveAsync(RankingItem item, CancellationToken cancellationToken);
}
