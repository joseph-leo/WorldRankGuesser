namespace SportsRankingService.Services;

/// <summary>Fetches a URL as text. Returns null for a non-success status or transport failure, never throws for those.</summary>
public interface IHttpFetcher
{
    /// <summary>Matched against <see cref="Models.RankingItem.Fetcher"/>, case-insensitively.</summary>
    string Name { get; }

    Task<string?> GetStringAsync(string url, CancellationToken cancellationToken);
}
