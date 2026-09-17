namespace SportsRankingService.Parsing;

/// <summary>
/// One row extracted from a federation feed: a position and the ISO3 country code, plus what the
/// feed says about that row. <paramref name="TeamName"/> is the country name as the federation
/// writes it (null when the feed gives only a code). <paramref name="Competitor"/> is the athlete,
/// pair or group the row belongs to when the federation ranks people rather than countries.
/// <paramref name="Points"/> is the federation's headline points or rating on its own scale.
/// Parsers produce one entry per ranked entity; the pipeline collapses them to countries and
/// stamps Sport/Event/Gender from configuration.
/// </summary>
public sealed record RankEntry(short Position, string ISO3, string? TeamName = null, string? Competitor = null, decimal? Points = null);
