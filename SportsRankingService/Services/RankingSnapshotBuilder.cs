using SportsRankingService.Models;
using SportsRankingService.Parsing;

namespace SportsRankingService.Services;

/// <summary>Turns parser output into a <see cref="RankingSnapshot"/>: orders, applies Take, stamps the item and decides the date.</summary>
public static class RankingSnapshotBuilder
{
    public static RankingSnapshot Build(RankingItem item, ParsedRanking parsed, DateOnly? resolvedDate, DateOnly today)
    {
        // The payload's own date is the most specific; a resolver's date came from the same federation's release list.
        DateOnly? federationDate = parsed.RankingDate ?? resolvedDate;

        IEnumerable<RankEntry> entries = parsed.Entries.OrderBy(e => e.Position);
        if (item.Take is int take)
        {
            // By position, not by row count, so a doubles pair sharing a position is never cut in half.
            entries = entries.Where(e => e.Position <= take);
        }

        return new RankingSnapshot(
            item.Sport,
            item.Event,
            item.Gender,
            federationDate ?? today,
            IsFederationDate: federationDate is not null,
            entries.ToList());
    }
}
