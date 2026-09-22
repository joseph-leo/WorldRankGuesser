using SportsRankingService.Models;

namespace SportsRankingService.Services;

/// <summary>One configured feed end to end: resolve, fetch, parse, build the snapshot. See <see cref="RankingSourceRunner"/>.</summary>
public interface IRankingSourceRunner
{
    Task<RankingSnapshot?> RunAsync(RankingItem item, CancellationToken cancellationToken);
}
