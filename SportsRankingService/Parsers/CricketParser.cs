using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SportsRankingService.Models;
using SportsRankingService.Utilities;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;


namespace SportsRankingService.Parsers
{
    public class CricketParser(ILogger<CricketParser> logger) : IParser
    {
        private readonly ILogger<CricketParser> _logger = logger;

        public IEnumerable<IRanking> ParseResponse(string response, RankingItem rankingItem)
        {
            try
            {
                List<SportsRanking> rankings = [];

                JObject json = JObject.Parse(response);

                JToken data = json["data"].NotNullOrEmpty();
                JToken batRank = data["bat-rank"].NotNullOrEmpty();
                JArray ranks = JArray.Parse(batRank["rank"].NotNullOrEmpty().ToString());

                foreach (JToken rank in ranks)
                {
                    short position = rank["no"].NotNullOrEmpty().Value<short>();
                    string countryCode = rank["shortname"].NotNullOrEmpty().Value<string>().NotNullOrEmpty();
                    string ISO3 = ToISO3(countryCode);

                    SportsRanking sportsRanking = new(rankingItem.Gender, rankingItem.Event, rankingItem.Sport, position, ISO3);

                    rankings.Add(sportsRanking);
                }

                return rankings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", rankingItem.Sport);
                return [];
            }

        }

        /// <summary>
        /// ICC "shortname" is a team code, not a country code: women's teams carry a "-W" suffix
        /// and a few teams use two-letter abbreviations. West Indies (WI) has no ISO3 code and is kept as-is.
        /// </summary>
        private static string ToISO3(string iccShortName)
        {
            string code = iccShortName.Trim().ToUpperInvariant();

            int suffix = code.IndexOf('-');
            if (suffix > 0)
            {
                code = code[..suffix];
            }

            return IccCodes.TryGetValue(code, out string? iso3) ? iso3 : code.IOCToISO3();
        }

        private static readonly Dictionary<string, string> IccCodes = new()
        {
            { "SA", "ZAF" },
            { "NZ", "NZL" },
            { "SL", "LKA" },
            { "HK", "HKG" },
            { "SRL", "LKA" },
        };
    }
}
