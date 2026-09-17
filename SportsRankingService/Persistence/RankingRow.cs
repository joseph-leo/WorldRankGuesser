namespace SportsRankingService.Persistence;

/// <summary>
/// One country's row in a <see cref="RankingRelease"/>. Ordinal is the position order; positions
/// can repeat when countries tie. A release holds one row per country.
/// </summary>
public class RankingRow
{
    public int ReleaseId { get; set; }

    public int Ordinal { get; set; }

    /// <summary>The federation's published position of the country's best entry.</summary>
    public short Position { get; set; }

    /// <summary>ISO 3166-1 alpha-3, except FIFA's home nations (ENG, SCO, WAL, NIR) and West Indies (WI).</summary>
    public required string ISO3 { get; set; }

    /// <summary>The country name: the federation's spelling, or the English region name when the feed gives only a code.</summary>
    public string? TeamName { get; set; }

    /// <summary>The athlete, pair or group this row stands on when the federation ranks people; null for team sports.</summary>
    public string? Competitor { get; set; }

    /// <summary>The federation's headline points or rating, on that federation's own scale; null when the feed has none.</summary>
    public decimal? Points { get; set; }

    /// <summary>How many of the country's athletes or pairs the ranking held; 1 for team sports.</summary>
    public int RankedEntrants { get; set; }

    public RankingRelease Release { get; set; } = null!;
}
