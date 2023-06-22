using HtmlAgilityPack;
using Newtonsoft.Json.Linq;
using WorldRankGuesser.Data;
using WorldRankGuesser.Helpers;

namespace WorldRankGuesser.Services
{
    public class SoccerService : ScrapeService<SoccerRank, SoccerRank>
    {
        protected override List<SoccerRank> ParseRanks(string response)
        {
            JObject json = JObject.Parse(response);
            List<JToken> results = json["rankings"].Children().ToList();

            List<SoccerRank> rankings = new List<SoccerRank>();
            foreach (JToken result in results)
            {
                string? countryCode = result["rankingItem"]["countryCode"].Value<string>();
                string? countryName = result["rankingItem"]["name"].Value<string>();
                int position = result["rankingItem"]["rank"].Value<int>();

                rankings.Add(new SoccerRank
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
