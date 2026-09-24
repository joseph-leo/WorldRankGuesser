namespace SportsRankingService.Configuration;

/// <summary>
/// The Worker in proxy/ that feeds with <c>"Fetcher": "Proxy"</c> are requested through. Bound from the <c>Proxy</c>
/// configuration section: in Azure the Job's environment variables <c>Proxy__Url</c> and <c>Proxy__Token</c>; locally
/// unset, which makes those feeds go direct (fine from a residential address).
/// </summary>
public sealed class ProxyOptions
{
    public const string SectionName = "Proxy";

    /// <summary>The Worker's origin, e.g. https://wrg-proxy.foweeti.workers.dev. Null, empty or blank: no proxy.</summary>
    public string? Url { get; set; }

    /// <summary>The shared token the Worker checks in the X-Proxy-Token header.</summary>
    public string? Token { get; set; }
}
