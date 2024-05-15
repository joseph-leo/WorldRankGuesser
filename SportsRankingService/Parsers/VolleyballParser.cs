using Newtonsoft.Json.Linq;
using SportsRankingService.Models;
using SportsRankingService.Utilities;

namespace SportsRankingService.Parsers
{
    public class VolleyballParser(ILogger<VolleyballParser> logger) : IParser
    {
        private readonly ILogger<VolleyballParser> _logger = logger;

        public IEnumerable<IRanking> ParseResponse(string response, RankingItem rankingItem)
        {
            try
            {
                List<SportsRanking> rankings = [];

                JObject json = JObject.Parse(response);

                JToken teams = json["teams"].NotNullOrEmpty();

                foreach (JToken team in teams)
                {
                    string countryCode = team["federationCode"].NotNullOrEmpty().Value<string>().NotNullOrEmpty();
                    //string countryName = team["federationName"].NotNullOrEmpty().Value<string>().NotNullOrEmpty();
                    short position = team["rankToDisplay"].NotNullOrEmpty().Value<short>();

                    string ISO3 = countryCode.Trim().IOCToISO3();

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
