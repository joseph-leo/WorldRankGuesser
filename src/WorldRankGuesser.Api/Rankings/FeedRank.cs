namespace WorldRankGuesser.Api.Rankings;

/// <summary>A country's standing in one feed (one sport, event and gender).</summary>
public sealed record FeedRank(int EntryRank, int CountryRank, string Sport, string? Event, string Gender, string? Competitor);

/// <summary>A country's best feed in a category under each rank mode. The two may be different feeds.</summary>
public sealed record CategoryRank(FeedRank BestByEntry, FeedRank BestByCountry);
