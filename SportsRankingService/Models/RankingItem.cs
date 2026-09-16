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

        /// <summary>Keep only the first N entries by position; null keeps everything.</summary>
        public int? Take { get; set; }

        /// <summary>False leaves the item in the file for reference without fetching it.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Free text for humans, e.g. why an item is disabled.</summary>
        public string? Note { get; set; }
    }
}
