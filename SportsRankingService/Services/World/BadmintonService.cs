using HtmlAgilityPack;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using SportsRankingService.Interfaces;
using SportsRankingService.Models;
using SportsRankingService.Utilities;
using System.Globalization;

namespace SportsRankingService.Services.World
{
    public class BadmintonService(ILogger<BadmintonService> logger) : WorldRankService(logger)
    {
        public override IEnumerable<IRanking> ParseResponse(string response, IRanking prototype)
        {
            try
            {
                List<SportsRanking> rankings = new List<SportsRanking>();

                JArray json = JArray.Parse(response).NotNullOrEmpty();

                foreach (JToken row in json)
                {
                    string? countryValue = row["country"].NotNullOrEmpty().Value<string>();
                    string countryCode = countryValue?.Length == 3 ? countryValue : countryValue.NotNullOrEmpty().Split('/')[0];
                    string ISO3 = countryCode.IOCToISO3();

                    short position = row["rank"].NotNullOrEmpty().Value<short>();

                    IRanking ranking = prototype.ShallowCopy();
                    ranking.AddRemaingProps(position, ISO3);

                    rankings.Add(ranking);
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
