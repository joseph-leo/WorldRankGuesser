using Newtonsoft.Json.Linq;
using System.Globalization;
using WorldRankGuesser.Data;

namespace WorldRankGuesser.Services
{
    public class BadmintonService : ScrapeService<BadmintonRank, BadmintonRank>
    {
        protected override List<BadmintonRank> ParseRanks(string response)
        {
            JObject json = JObject.Parse(response);
            List<JToken> leagues = json["rankings"].Children().ToList();

            List<BadmintonRank> rankings = new List<BadmintonRank>();
            foreach (JToken league in leagues)
            {
                string? gender = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(league["gender"].Value<string>());
                string? sport = GetSport(league["name"].Value<string>());

                foreach (JToken ranking in league["competitor_rankings"].Children())
                {
                    int position = ranking["rank"].Value<int>();
                    string? countryCode = ranking?["competitor"]?["country_code"]?.Value<string>();

                    if (countryCode != null)
                    {
                        rankings.Add(new BadmintonRank
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

        protected override async Task<string> CallUrlAsync(string fullUrl)
        {
            await Task.Delay(1001);
            string response = await base.CallUrlAsync(fullUrl);

            return response;
        }

        private string GetSport(string name)
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
