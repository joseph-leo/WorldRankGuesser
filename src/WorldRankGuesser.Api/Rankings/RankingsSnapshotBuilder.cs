using WorldRankGuesser.Api.Boards;
using WorldRankGuesser.Api.Configuration;
using WorldRankGuesser.Api.Countries;
using WorldRankGuesser.Api.Scoring;

namespace WorldRankGuesser.Api.Rankings;

public static class RankingsSnapshotBuilder
{
    public static RankingsSnapshot Build(
        IReadOnlyList<CountryRankingRow> rows,
        GameOptions options,
        int cap,
        CountryCatalog catalog,
        DateTimeOffset loadedAt)
    {
        var categoryBySport = options.Categories
            .SelectMany(c => c.Sports.Select(sport => (sport, c.Id)))
            .ToDictionary(x => x.sport, x => x.Id, StringComparer.OrdinalIgnoreCase);

        var ranks = new Dictionary<(string CategoryId, string Iso3), CategoryRank>();

        // Feeds in a fixed order, so a tie between feeds always resolves the same way.
        var feeds = rows
            .GroupBy(r => (r.Sport, r.Event, r.Gender))
            .OrderBy(g => g.Key.Sport, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Event ?? "", StringComparer.Ordinal)
            .ThenBy(g => g.Key.Gender, StringComparer.Ordinal);

        foreach (var feed in feeds)
        {
            if (!categoryBySport.TryGetValue(feed.Key.Sport, out var categoryId)) continue;

            var ordered = feed.OrderBy(r => r.Position).ToList();
            var countryRank = 0;
            short? previousPosition = null;

            for (var i = 0; i < ordered.Count; i++)
            {
                var row = ordered[i];
                if (row.Position != previousPosition)
                {
                    countryRank = i + 1;          // competition ranking: 1, 2, 2, 4
                    previousPosition = row.Position;
                }

                var feedRank = new FeedRank(row.Position, countryRank, row.Sport, row.Event, row.Gender, row.Competitor);
                Merge(ranks, (categoryId, row.ISO3), new CategoryRank(feedRank, feedRank));
            }
        }

        // A country's display name is the scraper's TeamName; a code with no row of its own has no name.
        var names = rows
            .Where(r => r.TeamName is not null)
            .GroupBy(r => r.ISO3, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().TeamName!, StringComparer.Ordinal);

        ApplyAliases(ranks, options, names);

        return new RankingsSnapshot(loadedAt, options.Categories, DrawableCountries(names, ranks, options, cap, catalog), ranks);
    }

    private static void Merge(
        Dictionary<(string, string), CategoryRank> ranks, (string, string) key, CategoryRank candidate)
    {
        ranks[key] = ranks.TryGetValue(key, out var existing)
            ? new CategoryRank(
                candidate.BestByEntry.EntryRank < existing.BestByEntry.EntryRank ? candidate.BestByEntry : existing.BestByEntry,
                candidate.BestByCountry.CountryRank < existing.BestByCountry.CountryRank ? candidate.BestByCountry : existing.BestByCountry)
            : candidate;
    }

    private static void ApplyAliases(
        Dictionary<(string CategoryId, string Iso3), CategoryRank> ranks,
        GameOptions options,
        Dictionary<string, string> names)
    {
        // Read from the ranks as published, so one rule's result never feeds another rule.
        var published = new Dictionary<(string, string), CategoryRank>(ranks);

        foreach (var rule in options.Aliases)
        {
            var categoryIds = rule.Categories.Count == 0 ? options.Categories.Select(c => c.Id) : rule.Categories;

            foreach (var categoryId in categoryIds)
            foreach (var target in rule.Targets)
            foreach (var source in rule.Sources)
            {
                if (published.TryGetValue((categoryId, source), out var sourceRank))
                {
                    // The rank is the source's, so it keeps the source's name: Jamaica's cricket rank is "West Indies".
                    var rankedAs = names.GetValueOrDefault(source);
                    Merge(ranks, (categoryId, target), new CategoryRank(
                        sourceRank.BestByEntry with { RankedAs = rankedAs },
                        sourceRank.BestByCountry with { RankedAs = rankedAs }));
                }
            }
        }
    }

    private static List<BoardCountry> DrawableCountries(
        Dictionary<string, string> names,
        Dictionary<(string CategoryId, string Iso3), CategoryRank> ranks,
        GameOptions options,
        int cap,
        CountryCatalog catalog)
    {
        var notDrawable = options.NotDrawable.ToHashSet(StringComparer.Ordinal);

        // Under the cap in every mode, so the pool is the same whichever mode is scoring. A country under the cap
        // nowhere would cost every player the cap wherever it was placed.
        var categoriesUnderCap = ranks
            .Where(r => Enum.GetValues<RankMode>().All(mode => ScoringEngine.Score(r.Value, mode, cap).Score < cap))
            .GroupBy(r => r.Key.Iso3, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var countries = new List<BoardCountry>();
        var missingIso2 = new List<string>();

        // A code with no name is not drawn.
        foreach (var (iso3, name) in names.OrderBy(n => n.Key, StringComparer.Ordinal))
        {
            if (notDrawable.Contains(iso3)) continue;
            if (categoriesUnderCap.GetValueOrDefault(iso3) < options.MinCategoriesUnderCap) continue;

            if (catalog.TryGetIso2(iso3, out var iso2))
            {
                countries.Add(new BoardCountry(iso3, iso2, name));
            }
            else
            {
                missingIso2.Add(iso3);
            }
        }

        if (missingIso2.Count > 0)
        {
            throw new InvalidOperationException(
                $"No ISO2 code for: {string.Join(", ", missingIso2)}. Add them to Countries/countries.json or to Game:NotDrawable.");
        }

        return countries;
    }
}
