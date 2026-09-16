using SportsRankingService.Models;
using SportsRankingService.Parsing;

namespace SportsRankingService.Services;

/// <summary>Turns parser output into database rows by stamping the item's Sport / Event / Gender.</summary>
public static class RankingMapper
{
    public static SportsRanking ToSportsRanking(RankEntry entry, RankingItem item) => new()
    {
        Sport = item.Sport,
        Event = item.Event,
        Gender = item.Gender,
        Position = entry.Position,
        ISO3 = entry.ISO3,
        TeamName = entry.TeamName,
    };

    public static List<SportsRanking> ToSportsRankings(IEnumerable<RankEntry> entries, RankingItem item) =>
        entries
            .OrderBy(e => e.Position)
            .Select(e => ToSportsRanking(e, item))
            .ToList();
}
