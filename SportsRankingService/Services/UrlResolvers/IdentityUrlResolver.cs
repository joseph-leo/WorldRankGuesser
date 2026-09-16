using SportsRankingService.Models;

namespace SportsRankingService.Services.UrlResolvers;

/// <summary>The default: the configured URL is fetched as-is.</summary>
public sealed class IdentityUrlResolver : IUrlResolver
{
    public const string ResolverName = "Identity";

    public string Name => ResolverName;

    public Task<string> ResolveAsync(RankingItem item, CancellationToken cancellationToken) => Task.FromResult(item.Url);
}
