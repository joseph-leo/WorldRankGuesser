namespace SportsRankingService.Parsing;

/// <summary>
/// One row extracted from a federation feed: a position and the ISO3 country code.
/// Parsers produce these; the pipeline stamps Sport/Event/Gender from configuration.
/// </summary>
public sealed record RankEntry(short Position, string ISO3, string? TeamName = null);
