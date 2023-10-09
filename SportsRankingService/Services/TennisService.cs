using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using SportsRankingService.Models;
using SportsRankingService.Utilities;
using System.Globalization;

namespace SportsRankingService.Services
{
    public class TennisService : ScrapeService
    {
        public TennisService(ILogger<TennisService> logger) : base(logger)
        {
        }

        protected override List<SportsRanking> ParseRanks(string response)
        {
            List<SportsRanking> rankings = new();
            JObject json;
            List<JToken> leagues;

            try
            {
                json = JObject.Parse(response);
                leagues = json["rankings"].Children().ToList();

                foreach (JToken league in leagues)
                {
                    string? gender = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(league["gender"].Value<string>());

                    foreach (JToken ranking in league["competitor_rankings"].Children())
                    {
                        short position = ranking["rank"].Value<short>();
                        string? countryCode = ranking?["competitor"]?["country_code"]?.Value<string>();

                        if (countryCode is not null)
                        {
                            rankings.Add(new SportsRanking
                            {
                                Gender = gender,
                                ISO3 = countryCode,
                                Position = position,
                                Sport = Sport,
                                RankDate = DateTime.Now,
                                
                            });
                        }
                    }
                }

                return rankings;
            }
            catch (Exception ex)
            {
                Log(ex, _logger);
                return new List<SportsRanking>();
            }
        }

        protected override async Task<string> CallUrlAsync(string fullUrl)
        {
            await Task.Delay(1001);
            string response = await base.CallUrlAsync(fullUrl);

            return response;
        }
    }
}
