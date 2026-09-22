using SportsRankingService.Services.UrlResolvers;

namespace SportsRankingService.Models
{
    /// <summary>One configured feed in serviceconfig.json: where to fetch it, how to parse it, and what to stamp on the rows.</summary>
    public class RankingItem
    {
        /// <summary>Stored on every row, e.g. "Field Hockey".</summary>
        public required string Sport { get; set; }

        /// <summary>Stored on every row, e.g. "Outdoor"; null when the sport has no sub-event.</summary>
        public string? Event { get; set; }

        /// <summary>Stored on every row: "Men", "Women" or "Mixed".</summary>
        public required string Gender { get; set; }

        /// <summary>URL to fetch. May contain a {0} placeholder filled by the <see cref="UrlResolver"/>.</summary>
        public required string Url { get; set; }

        /// <summary><see cref="Parsing.IRankingParser.SourceName"/> of the parser for this feed's response.</summary>
        public required string Source { get; set; }

        /// <summary><see cref="IUrlResolver.Name"/> of the strategy that turns <see cref="Url"/> into the request URL.</summary>
        public string UrlResolver { get; set; } = IdentityUrlResolver.ResolverName;

        /// <summary>
        /// <see cref="Services.IHttpFetcher.Name"/> of the client that fetches <see cref="Url"/>: "Http" (.NET HttpClient) unless a feed
        /// only answers another client (BWF: "Curl"). A resolver's preliminary request always goes through "Http".
        /// </summary>
        public string Fetcher { get; set; } = Services.HttpFetcher.FetcherName;

        /// <summary>
        /// Names one ranking inside a response that holds several, for parsers that need it (FIG: "&lt;series&gt; / &lt;apparatus&gt;",
        /// e.g. "World Cup / Vault"). Null for every other feed.
        /// </summary>
        public string? Selector { get; set; }

        /// <summary>False leaves the item in the file for reference without fetching it.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Free text for humans, e.g. why an item is disabled.</summary>
        public string? Note { get; set; }

        /// <summary>The feed's name in logs and on the command line: "Sport Event Gender", e.g. "Cricket ODI Women" or "Basketball Men".</summary>
        public string Describe() =>
            string.Join(" ", new[] { Sport, Event, Gender }.Where(s => !string.IsNullOrEmpty(s)));
    }
}
