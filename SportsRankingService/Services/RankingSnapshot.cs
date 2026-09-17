namespace SportsRankingService.Services;

/// <summary>
/// One feed's ranking as fetched on one run, ready to persist: the feed identity, the ranking
/// date (the federation's when it publishes one, otherwise the scrape date, which
/// <see cref="IsFederationDate"/> distinguishes) and one entry per country in position order.
/// This is the pipeline's output type; it knows nothing about the database.
/// </summary>
public sealed record RankingSnapshot(
    string Sport,
    string? Event,
    string Gender,
    DateOnly RankingDate,
    bool IsFederationDate,
    IReadOnlyList<RankingSnapshotEntry> Entries)
{
    /// <summary>"Sport Event Gender" for log lines, e.g. "Rugby Sevens Women" or "Basketball Men".</summary>
    public string Describe() =>
        string.Join(" ", new[] { Sport, Event, Gender }.Where(s => !string.IsNullOrEmpty(s)));
}
