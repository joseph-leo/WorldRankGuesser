namespace WorldRankGuesser.Api.Persistence;

public sealed class Player
{
    public Guid Id { get; set; }

    public string? Nickname { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>Set by sign-in (phase 4). A player is verified when ExternalSubject is set.</summary>
    public string? ExternalProvider { get; set; }

    public string? ExternalSubject { get; set; }

    public int CurrentStreak { get; set; }

    public int BestStreak { get; set; }

    public DateOnly? LastDailyDate { get; set; }
}
