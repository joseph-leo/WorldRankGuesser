using Newtonsoft.Json.Linq;
using WorldRankGuesser.Data;
using WorldRankGuesser.Helpers;

namespace WorldRankGuesser.Services
{
    public class VolleyballService : ScrapeService<VolleyballRank, VolleyballRank>
    {
        protected override List<VolleyballRank> ParseRanks(string response)
        {
            JObject json = JObject.Parse(response);
            List<JToken> results = json["teams"].Children().ToList();

            List<VolleyballRank> rankings = new();
            foreach (JToken result in results)
            {
                string? countryCode = result["federationCode"].Value<string>();
                string? countryName = result["federationName"].Value<string>();
                int position = result["rank"].Value<int>();

                rankings.Add(new VolleyballRank
                {
                    Gender = Gender,
                    Sport = Sport,
                    ISO3 = CountryUtil.GetISO3FromCode(countryCode, countryName),
                    Position = position
                });
            }

            return rankings;
        }
    }
}
