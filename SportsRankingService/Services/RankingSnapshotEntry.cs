namespace SportsRankingService.Services;

/// <summary>
/// One entry's row in a <see cref="RankingSnapshot"/>: a ranked athlete, doubles partner, group
/// or team. <paramref name="TeamName"/> is always the country name when one is known. Value
/// equality doubles as the builder's duplicate guard, so every stored fact is a positional
/// parameter of this record.
/// </summary>
public sealed record RankingSnapshotEntry(
    short Position,
    string ISO3,
    string? TeamName = null,
    string? Competitor = null,
    decimal? Points = null);
