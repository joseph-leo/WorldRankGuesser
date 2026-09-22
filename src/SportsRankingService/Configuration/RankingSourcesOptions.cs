using SportsRankingService.Models;

namespace SportsRankingService.Configuration;

/// <summary>Bound from serviceconfig.json: the list of feeds to fetch each run.</summary>
public sealed class RankingSourcesOptions
{
    public List<RankingItem> Rankings { get; set; } = [];
}
