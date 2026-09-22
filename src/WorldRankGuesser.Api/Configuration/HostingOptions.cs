namespace WorldRankGuesser.Api.Configuration;

public sealed class HostingOptions
{
    public const string Section = "Hosting";

    /// <summary>
    /// On only behind a proxy that rewrites X-Forwarded-For and X-Forwarded-Proto for every request (the Container Apps
    /// ingress): the per-IP rate limit then keys on the caller's address instead of the proxy's. Off, the default, a
    /// directly exposed container ignores the headers, so no one can spoof an address.
    /// </summary>
    public bool TrustForwardedHeaders { get; set; }
}
