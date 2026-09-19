namespace WorldRankGuesser.Api.Configuration;

public sealed class RateLimitOptions
{
    public const string Section = "RateLimits";

    public int GameStartsPerPlayerPerHour { get; set; } = 30;

    public int GameStartsPerIpPerHour { get; set; } = 120;
}
