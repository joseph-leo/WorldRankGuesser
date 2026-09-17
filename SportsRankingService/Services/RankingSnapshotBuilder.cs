using SportsRankingService.Models;
using SportsRankingService.Parsing;
using SportsRankingService.Utilities;

namespace SportsRankingService.Services;

/// <summary>
/// Turns parser output into a <see cref="RankingSnapshot"/>: orders by position, collapses the
/// entries to one per country, fills a missing country name, applies Take, stamps the item and
/// decides the date.
/// </summary>
public static class RankingSnapshotBuilder
{
    public static RankingSnapshot Build(RankingItem item, ParsedRanking parsed, DateOnly? resolvedDate, DateOnly today)
    {
        // The payload's own date is the most specific; a resolver's date came from the same federation's release list.
        DateOnly? federationDate = parsed.RankingDate ?? resolvedDate;

        // A stable sort, so a country's tied entries and countries tied with each other keep feed order.
        // The first entry of each country is its best placed; the rest only count as entrants.
        IEnumerable<RankingSnapshotEntry> entries = parsed.Entries
            .OrderBy(e => e.Position)
            .GroupBy(e => e.ISO3)
            .Select(country => ToCountryRow(country.First(), country.Count()))
            .OrderBy(e => e.Position);

        if (item.Take is int take)
        {
            // By position, not by row count, so countries sharing the cut-off position are all kept.
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

    private static RankingSnapshotEntry ToCountryRow(RankEntry best, int entrants) =>
        new(best.Position,
            best.ISO3,
            best.TeamName ?? CountryUtil.GetCountryName(best.ISO3),
            best.Competitor,
            best.Points,
            entrants);
}
