using HtmlAgilityPack;
using Newtonsoft.Json.Linq;
using SportsRankingService.Interfaces;
using SportsRankingService.Models;
using SportsRankingService.Utilities;


namespace SportsRankingService.Services.World
{
    public class SoccerService(ILogger<SoccerService> logger) : WorldRankService(logger)
    {
        public override List<SportsRanking> ParseResponse(string response, IRanking prototype)
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
                    string ISO3 = countryCode.IOCToISO3();

                    rankings.Add(new SportsRanking
                    {
                        Position = position,
                        Gender = rankInfo.Gender,
                        Sport = rankInfo.Sport,
                        ISO3 = ISO3,
                        //CountryName = countryName,
                    });
                }

                return rankings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{sport}|{event}|{gender}", rankInfo.Sport, rankInfo.Event, rankInfo.Gender);
                return [];
            }

        }
    }
}
