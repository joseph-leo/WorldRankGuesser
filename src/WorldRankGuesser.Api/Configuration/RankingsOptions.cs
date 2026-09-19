namespace WorldRankGuesser.Api.Configuration;

public sealed class RankingsOptions
{
    public const string Section = "Rankings";

    public int RefreshMinutes { get; set; } = 60;
}
