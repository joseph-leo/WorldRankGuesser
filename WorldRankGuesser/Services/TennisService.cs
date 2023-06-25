using HtmlAgilityPack;
using Newtonsoft.Json.Linq;
using System.Globalization;
using WorldRankGuesser.Data;
using WorldRankGuesser.Helpers;

namespace WorldRankGuesser.Services
{
    public class TennisService : ScrapeService<TennisRank, TennisRank>
    {
        protected override List<TennisRank> ParseRanks(string response)
        {            
            JObject json = JObject.Parse(response);
            List<JToken> leagues = json["rankings"].Children().ToList();

            List<TennisRank> rankings = new List<TennisRank>();
            foreach (JToken league in leagues)
            {
                string? gender = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(league["gender"].Value<string>());

                foreach (JToken ranking in league["competitor_rankings"].Children())
                {
                    int position = ranking["rank"].Value<int>();
                    string? countryCode = ranking?["competitor"]?["country_code"]?.Value<string>();

                    if (countryCode != null)
                    {
                        rankings.Add(new TennisRank
                        {
                            Gender = gender,
                            ISO3 = countryCode,
                            Position = position,
                            Sport = Sport
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
    }
}
