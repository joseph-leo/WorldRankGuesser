using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using SportsRankingService.Models;
using SportsRankingService.Utilities;
using System.Globalization;

namespace SportsRankingService.Services
{
    public class BadmintonService : ScrapeService
    {
        public BadmintonService(ILogger<ScrapeService> logger) : base(logger)
        {
        }

        protected override List<SportsRanking> ParseRanks(string response)
        {
            try
            {
                JObject json = JObject.Parse(response);
                List<JToken> leagues = json["rankings"].Children().ToList();

                List<SportsRanking> rankings = new List<SportsRanking>();
                foreach (JToken league in leagues)
                {
                    string? gender = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(league["gender"].Value<string>());
                    string? sport = GetSport(league["name"].Value<string>());

                    foreach (JToken ranking in league["competitor_rankings"].Children())
                    {
                        short position = ranking["rank"].Value<short>();
                        string? countryCode = ranking?["competitor"]?["country_code"]?.Value<string>();

                        if (countryCode != null)
                        {
                            rankings.Add(new SportsRanking
                            {
                                Gender = gender,
                                ISO3 = countryCode,
                                Position = position,
                                Sport = sport
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

        private static string GetSport(string name)
        {
            Dictionary<string, string> sportMap = new()
            {
                {"bwf_men_singles_world_ranking", "Singles Badminton" },
                {"bwf_men_doubles_world_ranking", "Doubles Badminton" },
                {"bwf_women_singles_world_ranking", "Singles Badminton" },
                {"bwf_women_doubles_world_ranking", "Doubles Badminton" }
            };

            return sportMap[name];
        }
    }
}
