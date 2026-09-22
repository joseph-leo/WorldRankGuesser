using SportsRankingService.Models;
using SportsRankingService.Parsing;
using SportsRankingService.Utilities;

namespace SportsRankingService.Services;

/// <summary>
/// Turns parser output into a <see cref="RankingSnapshot"/>: orders the entries by position,
/// names the country from its code, rejects equal duplicates, stamps the item and decides the date.
/// </summary>
public static class RankingSnapshotBuilder
{
    public static RankingSnapshot Build(RankingItem item, ParsedRanking parsed, DateOnly? resolvedDate, DateOnly today)
    {
        // The payload's own date is the most specific; a resolver's date came from the same federation's release list.
        DateOnly? federationDate = parsed.RankingDate ?? resolvedDate;

        // A stable sort, so entries tied on position keep feed order.
        List<RankingSnapshotEntry> entries = parsed.Entries
            .OrderBy(e => e.Position)
            .Select(e => new RankingSnapshotEntry(e.Position, e.ISO3, CountryName(item, e.ISO3), e.Competitor, e.Points))
            .ToList();

        // Equal rows carry no distinguishing fact, so they are always a feed or parser bug:
        // distinct entities differ at least in competitor (see the 2026-09-17 spec).
        HashSet<RankingSnapshotEntry> seen = [];
        foreach (RankingSnapshotEntry entry in entries)
        {
            if (!seen.Add(entry))
            {
                throw new ParseException(item.Source, $"duplicate entry {entry}");
            }
        }

        return new RankingSnapshot(
            item.Sport,
            item.Event,
            item.Gender,
            federationDate ?? today,
            IsFederationDate: federationDate is not null,
            entries);
    }

    // The name is the table's, never the feed's, so every feed spells a country the same way. A code
    // the table lacks is a new country or an unmapped federation code: fail loudly rather than store it nameless.
    private static string CountryName(RankingItem item, string iso3) =>
        CountryUtil.TryGetCountryName(iso3, out string? name)
            ? name!
            : throw new ParseException(item.Source, $"no country name for code '{iso3}'");
}
