using WorldRankGuesser.Api.Boards;
using WorldRankGuesser.Api.Persistence;

namespace WorldRankGuesser.Api.Games;

/// <summary>
/// The only code that decides what a response reveals: which category each past country went to, the current
/// country, and nothing else until the game is complete. What a pick scored stays hidden until then too.
/// </summary>
public static class GameStateMapper
{
    public static GameStateDto ToDto(Game game, DateTimeOffset now)
    {
        var content = game.Board.Content;
        var complete = game.CompletedAt is not null;

        var picks = game.Picks
            .OrderBy(p => p.TurnIndex)
            .Select(p =>
            {
                var categoryIndex = IndexOfCategory(content, p.CategoryId);
                return new PickDto(
                    p.TurnIndex,
                    p.CategoryId,
                    ToDto(content.Countries[p.TurnIndex]),
                    complete ? p.Score : null,
                    p.WasLate,
                    complete ? ToDto(content.Cells[p.TurnIndex][categoryIndex]) : null);
            })
            .ToList();

        return new GameStateDto(
            game.Id,
            game.Mode.ToString(),
            game.DailyDate,
            game.Board.RankMode.ToString(),
            game.Board.Cap,
            content.Categories.Select(c => new CategoryDto(c.Id, c.Name)).ToList(),
            picks,
            complete ? null : ToDto(content.Countries[game.TurnIndex]),
            complete ? null : game.TurnDeadline,
            now,
            complete,
            game.TotalScore,
            complete ? game.Board.OptimalScore : null,
            complete ? ToGrid(content) : null);
    }

    public static int IndexOfCategory(BoardContent content, string categoryId)
    {
        for (var i = 0; i < content.Categories.Count; i++)
        {
            if (content.Categories[i].Id == categoryId) return i;
        }

        return -1;
    }

    private static GridDto ToGrid(BoardContent content)
    {
        var scores = content.Cells.Select(IReadOnlyList<int> (row) => row.Select(cell => cell.Score).ToList()).ToList();

        // The board is immutable, so solving it again on read gives the assignment behind the stored OptimalScore.
        var optimal = OptimalAssignment.Solve(scores).CategoryOfCountry;

        return new GridDto(
            [.. content.Countries.Select(ToDto)],
            [.. content.Cells.Select(IReadOnlyList<CellDto>(row) => [.. row.Select(ToDto)])],
            [.. scores.Select(row => content.Categories[IndexOfLowest(row)].Id)],
            [.. optimal.Select(category => content.Categories[category].Id)]);
    }

    /// <summary>The first of equals, so a tie goes to the earlier category.</summary>
    private static int IndexOfLowest(IReadOnlyList<int> scores)
    {
        var lowest = 0;
        for (var i = 1; i < scores.Count; i++)
        {
            if (scores[i] < scores[lowest]) lowest = i;
        }

        return lowest;
    }

    private static CountryDto ToDto(BoardCountry c) => new(c.Iso3, c.Iso2, c.Name);

    private static CellDto ToDto(BoardCell c) =>
        new(c.Score, c.CountryRank, c.EntryRank, c.Unranked, c.Sport, c.Event, c.Gender, c.Competitor, c.RankedAs);
}
