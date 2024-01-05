using HtmlAgilityPack;
using Newtonsoft.Json.Linq;
using SportsRankingService.Interfaces;
using SportsRankingService.Models;
using SportsRankingService.Utilities;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;


namespace SportsRankingService.Services.World
{
    public class CricketService(ILogger<CricketService> logger) : WorldRankService(logger)
    {
        public override List<SportsRanking> ParseResponse(string response, IRanking prototype)
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
                    string ISO3 = countryCode.IOCToISO3();

                    rankings.Add(new SportsRanking
                    {
                        Gender = rankInfo.Gender,
                        Sport = rankInfo.Sport,
                        Event = rankInfo.Event,
                        ISO3 = ISO3,
                        Position = position,
                        //CountryName = countryName
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
