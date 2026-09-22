namespace WorldRankGuesser.Api.Rankings;

/// <summary>
/// A country's standing in one feed (one sport, event and gender). RankedAs names the team the rank belongs to
/// when an alias passed it on ("West Indies" for Jamaica's cricket rank); null when the rank is the country's own.
/// </summary>
public sealed record FeedRank(
    int EntryRank,
    int CountryRank,
    string Sport,
    string? Event,
    string Gender,
    string? Competitor,
    string? RankedAs = null);

/// <summary>A country's best feed in a category under each rank mode. The two may be different feeds.</summary>
public sealed record CategoryRank(FeedRank BestByEntry, FeedRank BestByCountry);
