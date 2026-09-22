namespace SportsRankingService.Persistence;

/// <summary>
/// One published ranking table for one feed. A release is immutable once written; a re-scrape that
/// produces the same content only moves <see cref="LastSeenAt"/>. All releases of a feed are its
/// history, and the one with the greatest <see cref="Id"/> is current.
/// </summary>
public class RankingRelease
{
    public int Id { get; set; }

    public required string Sport { get; set; }

    public string? Event { get; set; }

    public required string Gender { get; set; }

    /// <summary>The federation's ranking date when <see cref="IsFederationDate"/>, otherwise the scrape date.</summary>
    public DateOnly RankingDate { get; set; }

    public bool IsFederationDate { get; set; }

    /// <summary>SHA-256 of the rows plus the federation date; see RankingContentHash.</summary>
    public required byte[] ContentHash { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    public List<RankingRow> Rows { get; set; } = [];
}
