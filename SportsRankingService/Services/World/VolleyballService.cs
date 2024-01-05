using Newtonsoft.Json.Linq;
using SportsRankingService.Interfaces;
using SportsRankingService.Models;
using SportsRankingService.Utilities;

namespace SportsRankingService.Services.World
{
    public class VolleyballService(ILogger<VolleyballService> logger) : WorldRankService(logger)
    {
        public override List<SportsRanking> ParseResponse(string response, IRanking prototype)
        {
            try
            {
                List<SportsRanking> rankings = [];

                JObject json = JObject.Parse(response);

                JToken teams = json["teams"].NotNullOrEmpty();

                foreach (JToken team in teams)
                {
                    string countryCode = team["federationCode"].NotNullOrEmpty().Value<string>().NotNullOrEmpty();
                    string countryName = team["federationName"].NotNullOrEmpty().Value<string>().NotNullOrEmpty();
                    short position = team["rankToDisplay"].NotNullOrEmpty().Value<short>();

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
                _logger.LogError(ex, "{Message}", rankInfo.Sport);
                return [];
            }

        }
    }
}
