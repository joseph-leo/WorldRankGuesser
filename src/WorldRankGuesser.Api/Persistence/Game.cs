namespace WorldRankGuesser.Api.Persistence;

public sealed class Game
{
    public Guid Id { get; set; }

    public Guid PlayerId { get; set; }

    public Player Player { get; set; } = null!;

    public long BoardId { get; set; }

    public Board Board { get; set; } = null!;

    public GameMode Mode { get; set; }

    /// <summary>Copied from the board so the one-attempt-per-day index and the leaderboard need no join.</summary>
    public DateOnly? DailyDate { get; set; }

    /// <summary>Index of the country the player is deciding on; equals the number of picks made.</summary>
    public int TurnIndex { get; set; }

    public DateTimeOffset? TurnDeadline { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public int? TotalScore { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public List<Pick> Picks { get; set; } = [];
}
