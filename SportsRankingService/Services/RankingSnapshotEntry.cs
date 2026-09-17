namespace SportsRankingService.Services;

/// <summary>
/// One country's row in a <see cref="RankingSnapshot"/>: the country's best-placed entry in the
/// feed, with how many of its athletes or pairs the ranking held (<paramref name="RankedEntrants"/>,
/// 1 for team sports). <paramref name="TeamName"/> is always the country name when one is known.
/// </summary>
public sealed record RankingSnapshotEntry(
    short Position,
    string ISO3,
    string? TeamName = null,
    string? Competitor = null,
    decimal? Points = null,
    int RankedEntrants = 1);
