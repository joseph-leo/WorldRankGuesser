using WorldRankGuesser.Api.Boards;
using WorldRankGuesser.Api.Configuration;

namespace WorldRankGuesser.Api.Persistence;

/// <summary>Immutable once saved. A daily board has a DailyDate; a practice board does not.</summary>
public sealed class Board
{
    public long Id { get; set; }

    public DateOnly? DailyDate { get; set; }

    public RankMode RankMode { get; set; }

    public int Cap { get; set; }

    public DateTimeOffset RankingsLoadedAt { get; set; }

    public int OptimalScore { get; set; }

    public BoardContent Content { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
}
