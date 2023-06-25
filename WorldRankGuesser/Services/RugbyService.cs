using HtmlAgilityPack;
using Newtonsoft.Json.Linq;
using WorldRankGuesser.Data;
using WorldRankGuesser.Helpers;

namespace WorldRankGuesser.Services
{
    public class RugbyService : ScrapeService<RugbyRank, RugbyRank>
    {
        protected override List<RugbyRank> ParseRanks(string response)
        {
            return Sport switch
            {
                "Rugby Union" => ParseUnion(response),
                "Rugby Sevens" => ParseSevens(response),
                _ => new List<RugbyRank>()
            };
        }

        private List<RugbyRank> ParseUnion(string response)
        {
            JObject json = JObject.Parse(response);
            List<JToken> results = json["entries"].Children().ToList();

            List<RugbyRank> rankings = new List<RugbyRank>();
            foreach (JToken result in results)
            {
                string? countryCode = result["team"]["countryCode"].Value<string>();
                string? countryName = result["team"]["name"].Value<string>();
                int position = result["pos"].Value<int>();

                rankings.Add(new RugbyRank
                {
                    Gender = Gender,
                    Sport = Sport,
                    ISO3 = CountryUtil.GetISO3FromCode(countryCode, countryName),
                    Position = position
                });
            }

            return rankings;
        }

        private List<RugbyRank> ParseSevens(string response)
        {
            HtmlDocument? htmlDocument = new();
            htmlDocument.LoadHtml(response);
            HtmlNode? table = htmlDocument.DocumentNode.SelectSingleNode("//table");

            List<RugbyRank> rankings = new();

            if (table != null)
            {
                foreach (var row in table.SelectSingleNode("tbody").SelectNodes("tr"))
                {
                    List<string> cells = row.SelectNodes("td").Select(x => x.InnerText.Trim()).ToList();
                    string countryCode = cells[1].Split('\n')[1].Trim();

                    if (cells != null)
                    {
                        if (int.TryParse(cells[0], out int rank))
                        {
                            rankings.Add(new RugbyRank
                            {
                                ISO3 = CountryUtil.IOCToISO3(countryCode),
                                Position = rank,
                                Sport = Sport,
                                Gender = Gender
                            });
                        }
                    }
                }
            }

            return rankings;
        }
    }
}
