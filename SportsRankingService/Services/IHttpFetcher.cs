namespace SportsRankingService.Services;

/// <summary>Fetches a URL as text. Returns null for a non-success status or transport failure, never throws for those.</summary>
public interface IHttpFetcher
{
    Task<string?> GetStringAsync(string url, CancellationToken cancellationToken);
}
