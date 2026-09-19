namespace WorldRankGuesser.Api.Persistence;

public sealed class Pick
{
    public Guid GameId { get; set; }

    public int TurnIndex { get; set; }

    public string CategoryId { get; set; } = "";

    public string ISO3 { get; set; } = "";

    public int Score { get; set; }

    public bool WasLate { get; set; }

    public DateTimeOffset PickedAt { get; set; }
}
