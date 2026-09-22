namespace SportsRankingService.Services;

public sealed class HttpFetcher(IHttpClientFactory httpClientFactory, ILogger<HttpFetcher> logger) : IHttpFetcher
{
    public const string ClientName = "federations";
    public const string FetcherName = "Http";

    public string Name => FetcherName;

    public async Task<string?> GetStringAsync(string url, CancellationToken cancellationToken)
    {
        logger.LogInformation("Fetching {Url}", url);

        try
        {
            HttpClient client = httpClientFactory.CreateClient(ClientName);
            using HttpResponseMessage response = await client.GetAsync(url, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync(cancellationToken);
            }

            logger.LogWarning("Fetch failed. {Url} returned {StatusCode}", url, (int)response.StatusCode);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogError(ex, "Fetch failed. {Url}", url);
            return null;
        }
    }
}
