namespace SportsRankingService.Persistence;

/// <summary>
/// One ranked entry's row in a <see cref="RankingRelease"/>. Ordinal is the storage order (position
/// order, ties by feed order); positions can repeat and a country can appear many times.
/// </summary>
public class RankingRow
{
    public int ReleaseId { get; set; }

    public int Ordinal { get; set; }

    /// <summary>The federation's published position of this entry.</summary>
    public short Position { get; set; }

    /// <summary>ISO 3166-1 alpha-3, except FIFA's home nations (ENG, SCO, WAL, NIR) and West Indies (WI).</summary>
    public required string ISO3 { get; set; }

    /// <summary>The country name from <see cref="Utilities.CountryNames"/> for the code; never the federation's spelling.</summary>
    public string? TeamName { get; set; }

    /// <summary>The athlete, pair or group this row stands on when the federation ranks people; null for team sports.</summary>
    public string? Competitor { get; set; }

    /// <summary>The federation's headline points or rating, on that federation's own scale; null when the feed has none.</summary>
    public decimal? Points { get; set; }

    public RankingRelease Release { get; set; } = null!;
}
