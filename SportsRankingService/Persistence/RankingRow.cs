namespace SportsRankingService.Persistence;

/// <summary>One entry of a <see cref="RankingRelease"/>. Ordinal is the feed order; positions can repeat (doubles partners).</summary>
public class RankingRow
{
    public int ReleaseId { get; set; }

    public int Ordinal { get; set; }

    public short Position { get; set; }

    /// <summary>ISO 3166-1 alpha-3, except FIFA's home nations (ENG, SCO, WAL, NIR) and West Indies (WI).</summary>
    public required string ISO3 { get; set; }

    public string? TeamName { get; set; }

    public RankingRelease Release { get; set; } = null!;
}
