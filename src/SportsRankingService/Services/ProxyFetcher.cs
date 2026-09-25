using Microsoft.Extensions.Options;
using SportsRankingService.Configuration;

namespace SportsRankingService.Services;

/// <summary>
/// Fetches through the Cloudflare Worker in proxy/, for a feed whose site refuses hosting addresses: www.wbsc.org sits
/// behind CloudFront, which answers 403 to Azure (2026-09-24) but serves Cloudflare's egress. The Worker forwards
/// GET /fetch?url=... to an allow-listed host and returns the upstream status and body unchanged, so a non-success
/// status is a failed fetch exactly as it would be directly, and the log names the target, not the Worker.
/// With no Proxy:Url configured (a run from the owner's machine) the request goes directly through the HTTP fetcher,
/// with one warning per run: from a residential address that works, and in Azure the feed's 403 plus the warning say
/// what is missing.
/// </summary>
public sealed class ProxyFetcher : IHttpFetcher
{
    public const string FetcherName = "Proxy";
    public const string ClientName = "proxy";
    public const string TokenHeader = "X-Proxy-Token";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHttpFetcher _direct;
    private readonly ProxyOptions _options;
    private readonly ILogger<ProxyFetcher> _logger;
    private int _warnedNoUrl;
    private int _warnedNoToken;

    public ProxyFetcher(IHttpClientFactory httpClientFactory, HttpFetcher direct, IOptions<ProxyOptions> options, ILogger<ProxyFetcher> logger)
        : this(httpClientFactory, (IHttpFetcher)direct, options.Value, logger)
    {
    }

    internal ProxyFetcher(IHttpClientFactory httpClientFactory, IHttpFetcher direct, ProxyOptions options, ILogger<ProxyFetcher> logger)
    {
        _httpClientFactory = httpClientFactory;
        _direct = direct;
        _options = options;
        _logger = logger;
    }

    public string Name => FetcherName;

    public async Task<string?> GetStringAsync(string url, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Url))
        {
            if (Interlocked.Exchange(ref _warnedNoUrl, 1) == 0)
            {
                _logger.LogWarning("Proxy:Url is not configured: feeds with Fetcher \"{Fetcher}\" are fetched directly, which a hosting address may be refused for", FetcherName);
            }

            return await _direct.GetStringAsync(url, cancellationToken);
        }

        // A rotation slip (the Job deployed without the secret) shows as six 401 lines; this names the cause once.
        if (string.IsNullOrWhiteSpace(_options.Token) && Interlocked.Exchange(ref _warnedNoToken, 1) == 0)
        {
            _logger.LogWarning("Proxy:Token is not configured: the proxy at {ProxyUrl} will answer 401", _options.Url);
        }

        _logger.LogInformation("Fetching {Url} through the proxy", url);
        string request = $"{_options.Url.TrimEnd('/')}/fetch?url={Uri.EscapeDataString(url)}";

        try
        {
            HttpClient client = _httpClientFactory.CreateClient(ClientName);
            using HttpRequestMessage message = new(HttpMethod.Get, request);
            message.Headers.TryAddWithoutValidation(TokenHeader, _options.Token ?? "");
            using HttpResponseMessage response = await client.SendAsync(message, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync(cancellationToken);
            }

            _logger.LogWarning("Fetch failed. {Url} returned {StatusCode} through the proxy", url, (int)response.StatusCode);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            _logger.LogError(ex, "Fetch failed through the proxy. {Url}", url);
            return null;
        }
    }
}
