using HtmlAgilityPack;
using System.Globalization;
using System.Reflection;
using WorldRankGuesser.Data;
using WorldRankGuesser.Helpers;

namespace WorldRankGuesser.Services
{
    public class CricketService : ScrapeService<CricketRank, CricketRank>
    {
        protected override List<CricketRank> ParseRanks(string response)
        {
            List<CricketRank> rankings = new();

            HtmlDocument? htmlDocument = new();
            htmlDocument.LoadHtml(response);

            var table = htmlDocument.DocumentNode.SelectSingleNode("//table");
            var rows = table.SelectSingleNode("tbody").SelectNodes("tr");

            foreach (var row in rows)
            {
                List<string> cells = row.SelectNodes("td").Select(x => x.InnerText.Trim()).ToList();
                string countryName = cells[1].Split('\n')[0].Trim();
                if (int.TryParse(cells[0], out int position))
                {
                    if (countryName == "West Indies")
                    {
                        rankings.Add(new CricketRank
                        {
                            Gender = Gender,
                            Format = Sport,
                            Sport = "Cricket",
                            ISO3 = "WI",
                            Position = position
                        });
                    }
                    else
                    {
                        string ISO3 = CountryUtil.GetISO3FromCountry(countryName);

                        rankings.Add(new CricketRank
                        {
                            Gender = Gender,
                            Format = Sport,
                            Sport = "Cricket",
                            ISO3 = ISO3,
                            Position = position
                        });
                    }
                }
                
            }
            return rankings;
        }

        protected override List<CricketRank> GetCountryRank(List<CricketRank> allRanks, string ISO3)
        {
            return CountryUtil.WestIndiesISO3s.Contains(ISO3) ? allRanks.Where(x => x.ISO3 == "WI").ToList() : base.GetCountryRank(allRanks, ISO3);
        }
    }
}
