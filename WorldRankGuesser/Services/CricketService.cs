using HtmlAgilityPack;
using System.Globalization;
using System.Reflection;
using WorldRankGuesser.Data;
using WorldRankGuesser.Helpers;

namespace WorldRankGuesser.Services
{
    public class CricketService : ScrapeService<CricketRank, CricketRank>
    {
        protected override List<CricketRank> ParseRanks(HtmlDocument htmlDoc)
        {
            List<CricketRank> rankings = new();

            var table = htmlDoc.DocumentNode.SelectSingleNode("//table");
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
                            IOC = "WI",
                            Position = position
                        });
                    }
                    else
                    {
                        string ISO3 = CountryUtil.GetISO3(countryName);
                        string IOC = ISO3.ToIOC();

                        rankings.Add(new CricketRank
                        {
                            Gender = Gender,
                            Format = Sport,
                            Sport = "Cricket",
                            IOC = IOC,
                            Position = position
                        });
                    }
                }
                
            }
            return rankings;
        }

        protected override List<CricketRank> GetCountryRank(List<CricketRank> allRanks, string ISO3)
        {
            return CountryUtil.WestIndiesIOCs.Contains(ISO3.ToIOC()) ? allRanks.Where(x => x.IOC == "WI").ToList() : base.GetCountryRank(allRanks, ISO3);
        }
    }
}
