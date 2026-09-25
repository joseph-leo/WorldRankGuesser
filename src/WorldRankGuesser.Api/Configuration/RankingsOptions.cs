namespace WorldRankGuesser.Api.Configuration;

public sealed class RankingsOptions
{
    public const string Section = "Rankings";

    public int RefreshMinutes { get; set; } = 60;

    /// <summary>
    /// The shared secret that POST /api/rankings/refresh requires in X-Refresh-Token. Null (the default, and every
    /// local run) leaves the route unmapped. In Azure the Container App secret behind Rankings__RefreshToken.
    /// </summary>
    public string? RefreshToken { get; set; }
}
