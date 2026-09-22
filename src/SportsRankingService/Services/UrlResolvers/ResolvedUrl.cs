namespace SportsRankingService.Services.UrlResolvers;

/// <summary>
/// The URL to fetch for an item and, when the resolver had to discover a release date to build
/// it, that date. Null means the resolver learned nothing about the ranking date.
/// </summary>
public sealed record ResolvedUrl(string Url, DateOnly? RankingDate = null);
