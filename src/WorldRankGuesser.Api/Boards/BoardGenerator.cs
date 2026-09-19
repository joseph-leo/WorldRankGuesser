using WorldRankGuesser.Api.Configuration;
using WorldRankGuesser.Api.Rankings;
using WorldRankGuesser.Api.Scoring;

namespace WorldRankGuesser.Api.Boards;

public sealed record GeneratedBoard(BoardContent Content, int OptimalScore);

public static class BoardGenerator
{
    public static GeneratedBoard Generate(RankingsSnapshot snapshot, ScoringOptions scoring, Random random)
    {
        var categories = snapshot.Categories.Select(c => new BoardCategory(c.Id, c.Name)).ToList();
        var countries = Draw(snapshot.DrawableCountries, categories.Count, random);

        var cells = countries
            .Select(country => (IReadOnlyList<BoardCell>)categories
                .Select(category => ScoringEngine.Score(snapshot.Find(category.Id, country.Iso3), scoring.RankMode, scoring.Cap))
                .ToList())
            .ToList();

        var optimal = OptimalAssignment.MinTotal(cells.Select(row => row.Select(cell => cell.Score).ToList()).ToList());

        return new GeneratedBoard(new BoardContent(categories, countries, cells), optimal);
    }

    /// <summary>A uniform draw without replacement: the first <paramref name="count"/> steps of a Fisher-Yates shuffle.</summary>
    private static List<BoardCountry> Draw(IReadOnlyList<BoardCountry> pool, int count, Random random)
    {
        if (pool.Count < count)
        {
            throw new InvalidOperationException(
                $"Only {pool.Count} drawable countries for a board of {count}. Check the rankings data and Game:MinCategoriesRanked.");
        }

        var shuffled = pool.ToArray();
        for (var i = 0; i < count; i++)
        {
            var j = random.Next(i, shuffled.Length);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        return shuffled.Take(count).ToList();
    }
}
