namespace SportsRankingService.Parsing;

/// <summary>
/// One row extracted from a federation feed: a position and the ISO3 country code, plus what the
/// feed says about that row. <paramref name="Competitor"/> is the athlete, pair or group the row
/// belongs to when the federation ranks people rather than countries. <paramref name="Points"/> is
/// the federation's headline points or rating on its own scale. There is deliberately no country
/// name: the pipeline names every country from its code (<see cref="Utilities.CountryNames"/>) so
/// a federation's own spelling never reaches storage. Parsers produce one entry per ranked entity;
/// the pipeline stores them all and stamps Sport/Event/Gender from configuration.
/// </summary>
public sealed record RankEntry(short Position, string ISO3, string? Competitor = null, decimal? Points = null);
