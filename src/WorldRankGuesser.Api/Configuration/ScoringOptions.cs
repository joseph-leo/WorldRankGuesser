namespace WorldRankGuesser.Api.Configuration;

public enum RankMode
{
    /// <summary>The federation's published position of the country's best entry (an athlete's world rank).</summary>
    Entry,

    /// <summary>The country's rank among countries in the feed.</summary>
    Country,
}

public sealed class ScoringOptions
{
    public const string Section = "Scoring";

    public RankMode RankMode { get; set; } = RankMode.Country;

    /// <summary>The highest score a pick can cost; also the score of an unranked country.</summary>
    public int Cap { get; set; } = 150;
}
