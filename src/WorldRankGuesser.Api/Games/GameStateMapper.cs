using WorldRankGuesser.Api.Boards;
using WorldRankGuesser.Api.Persistence;

namespace WorldRankGuesser.Api.Games;

/// <summary>
/// The only code that decides what a response reveals: past picks, the current country, and nothing else
/// until the game is complete.
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
                    p.Score,
                    p.WasLate,
                    ToDto(content.Cells[p.TurnIndex][categoryIndex]));
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
            complete
                ? new GridDto(
                    content.Countries.Select(ToDto).ToList(),
                    content.Cells.Select(row => (IReadOnlyList<CellDto>)row.Select(ToDto).ToList()).ToList())
                : null);
    }

    public static int IndexOfCategory(BoardContent content, string categoryId)
    {
        for (var i = 0; i < content.Categories.Count; i++)
        {
            if (content.Categories[i].Id == categoryId) return i;
        }

        return -1;
    }

    private static CountryDto ToDto(BoardCountry c) => new(c.Iso3, c.Iso2, c.Name);

    private static CellDto ToDto(BoardCell c) =>
        new(c.Score, c.CountryRank, c.EntryRank, c.Unranked, c.Sport, c.Event, c.Gender, c.Competitor);
}
