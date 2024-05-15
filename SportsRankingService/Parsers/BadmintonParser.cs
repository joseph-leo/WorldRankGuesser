using HtmlAgilityPack;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using SportsRankingService.Factories;
using SportsRankingService.Models;
using SportsRankingService.Utilities;
using System.Globalization;

namespace SportsRankingService.Parsers
{
    public class BadmintonParser(ILogger<BadmintonParser> logger) : IParser
    {
        private readonly ILogger<BadmintonParser> _logger = logger;
        public IEnumerable<IRanking> ParseResponse(string response, RankingItem rankingItem)
        {
            try
            {
                List<SportsRanking> rankings = [];

                JArray json = JArray.Parse(response).NotNullOrEmpty();

                foreach (JToken row in json)
                {
                    string countryValue = row["country"].NotNullOrEmpty().Value<string>().NotNullOrEmpty();
                    string countryCode = countryValue.Length == 3 ? countryValue : countryValue.Split('/')[0];
                    string ISO3 = countryCode.Trim().IOCToISO3();

                    short position = row["rank"].NotNullOrEmpty().Value<short>();

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
    }
}
