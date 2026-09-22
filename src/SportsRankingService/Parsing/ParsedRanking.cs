namespace SportsRankingService.Parsing;

/// <summary>
/// What a parser extracts from one feed response: the ranked entries and, when the feed publishes
/// one, the date the federation attached to the ranking. Null means the feed carries no usable
/// ranking date (some only stamp the time the file was generated).
/// </summary>
public sealed record ParsedRanking(IReadOnlyList<RankEntry> Entries, DateOnly? RankingDate = null);
