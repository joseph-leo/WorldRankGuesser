namespace WorldRankGuesser.Api.Rankings;

/// <summary>
/// One row of dbo.CurrentCountryRankings, the scraper's read model: a country's best-placed entry in one feed.
/// The view is owned by the SportsRankingService repo; this type is read-only.
/// </summary>
public sealed class CountryRankingRow
{
    public string Sport { get; init; } = "";

    public string? Event { get; init; }

    public string Gender { get; init; } = "";

    public DateOnly RankingDate { get; init; }

    public bool IsFederationDate { get; init; }

    public short Position { get; init; }

    public string ISO3 { get; init; } = "";

    public string? TeamName { get; init; }

    public string? Competitor { get; init; }

    public decimal? Points { get; init; }

    public int? RankedEntrants { get; init; }
}
