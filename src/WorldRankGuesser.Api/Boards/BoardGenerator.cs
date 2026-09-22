using WorldRankGuesser.Api.Configuration;
using WorldRankGuesser.Api.Rankings;
using WorldRankGuesser.Api.Scoring;

namespace WorldRankGuesser.Api.Boards;

/// <summary>CapPicksInOptimal is how many picks of the optimal assignment score the cap.</summary>
public sealed record GeneratedBoard(BoardContent Content, int OptimalScore, int CapPicksInOptimal);

public static class BoardGenerator
{
    /// <summary>Real rankings accept about 19 draws in 20, so this is only reached by a pool that cannot meet the limit.</summary>
    private const int MaxDraws = 50;

    /// <summary>
    /// Draws again while the optimal assignment has more than <paramref name="maxCapPicksInOptimal"/> picks scoring
    /// the cap. After <see cref="MaxDraws"/> draws the last board is returned as it is, so a game can always start;
    /// its CapPicksInOptimal tells the caller.
    /// </summary>
    public static GeneratedBoard Generate(RankingsSnapshot snapshot, ScoringOptions scoring, int maxCapPicksInOptimal, Random random)
    {
        var board = DrawBoard(snapshot, scoring, random);

        for (var draws = 1; board.CapPicksInOptimal > maxCapPicksInOptimal && draws < MaxDraws; draws++)
        {
            board = DrawBoard(snapshot, scoring, random);
        }

        return board;
    }

    private static GeneratedBoard DrawBoard(RankingsSnapshot snapshot, ScoringOptions scoring, Random random)
    {
        var categories = snapshot.Categories.Select(c => new BoardCategory(c.Id, c.Name)).ToList();
        var countries = Draw(snapshot.DrawableCountries, categories.Count, random);

        var cells = countries
            .Select(country => (IReadOnlyList<BoardCell>)categories
                .Select(category => ScoringEngine.Score(snapshot.Find(category.Id, country.Iso3), scoring.RankMode, scoring.Cap))
                .ToList())
            .ToList();

        // The same solve the results screen shows, so the count is of the optimal the player will see.
        var optimal = OptimalAssignment.Solve(cells.Select(row => row.Select(cell => cell.Score).ToList()).ToList());
        var capPicks = optimal.CategoryOfCountry.Where((category, country) => cells[country][category].Score >= scoring.Cap).Count();

        return new GeneratedBoard(new BoardContent(categories, countries, cells), optimal.Total, capPicks);
    }

    /// <summary>A uniform draw without replacement: the first <paramref name="count"/> steps of a Fisher-Yates shuffle.</summary>
    private static List<BoardCountry> Draw(IReadOnlyList<BoardCountry> pool, int count, Random random)
    {
        if (pool.Count < count)
        {
            throw new InvalidOperationException(
                $"Only {pool.Count} drawable countries for a board of {count}. Check the rankings data and Game:MinCategoriesUnderCap.");
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
