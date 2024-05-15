using HtmlAgilityPack;
using Newtonsoft.Json.Linq;
using SportsRankingService.Models;
using SportsRankingService.Utilities;


namespace SportsRankingService.Parsers
{
    public class SoccerParser(ILogger<SoccerParser> logger) : IParser
    {
        private readonly ILogger<SoccerParser> _logger = logger;

        public IEnumerable<IRanking> ParseResponse(string response, RankingItem rankingItem)
        {

            try
            {
                List<SportsRanking> rankings = [];

                JObject json = JObject.Parse(response);

                JToken allRanks = json["rankings"].NotNullOrEmpty();

                foreach (JToken result in allRanks)
                {
                    JToken team = result["rankingItem"].NotNullOrEmpty();

                    short? rank = team["rank"].NotNullOrEmpty().Value<short?>();

                    //Once ranks are null teams are unranked.
                    if (rank == null)
                        break;

                    short position = rank.Value;
                    string countryName = team["name"].NotNullOrEmpty().Value<string>().NotNullOrEmpty();
                    string countryCode = team["countryCode"].NotNullOrEmpty().Value<string>().NotNullOrEmpty();
                    string ISO3 = countryCode.Trim().IOCToISO3();

                    SportsRanking sportsRanking = new(rankingItem.Gender, rankingItem.Event, rankingItem.Sport, position, ISO3);

                    rankings.Add(sportsRanking);
                }

                return rankings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{sport}|{event}|{gender}", rankingItem.Sport, rankingItem.Event, rankingItem.Gender);
                return [];
            }

        }
    }
}
