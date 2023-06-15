using HtmlAgilityPack;
using System.Linq;
using System.Reflection;
using WorldRankGuesser.Data;
using WorldRankGuesser.Helpers;

namespace WorldRankGuesser.Services
{
    public class HockeyService : ScrapeService<HockeyRank, HockeyRank>
    {
        protected override List<HockeyRank> ParseRanks(HtmlDocument htmlDoc)
        {
            return Sport switch
            {
                "Field Hockey" => ParseFieldHockey(htmlDoc),
                "Ice Hockey" => ParseIceHockey(htmlDoc),
                _ => new List<HockeyRank>(),
            };
        }

        private List<HockeyRank> ParseFieldHockey(HtmlDocument htmlDoc)
        {
            List<HockeyRank> rankings = new();
            var table = htmlDoc.DocumentNode.SelectNodes("//a").Where(x => x.GetClasses().Contains("table-row"));

            foreach (var row in table)
            {
                var rowList = row.InnerText.Split("  ").Select(x => x.Trim()).ToList();
                string ISO3 = CountryUtil.GetISO3(rowList[1]);
                string IOC = CountryUtil.GetIOCMapping(ISO3);

                if (!rankings.Any(x => x.IOC == IOC) && int.TryParse(rowList[0], out int rank))
                {
                    rankings.Add(new HockeyRank
                    {
                        IOC = IOC,
                        Rank = rank,
                        Sport = Sport,
                        Gender = Gender
                    });
                }
            }

            return rankings;
        }

        private List<HockeyRank> ParseIceHockey(HtmlDocument htmlDoc)
        {
            List<HockeyRank> rankings = new();
            var tables = htmlDoc.DocumentNode.SelectNodes("//table");

            HtmlNode mensTable = tables[0];
            rankings.AddRange(ParseIceHockeyTable(mensTable, "Men"));

            HtmlNode womensTable = tables[1];
            rankings.AddRange(ParseIceHockeyTable(womensTable, "Women"));

            return rankings;
        }

        private List<HockeyRank> ParseIceHockeyTable(HtmlNode table, string gender)
        {
            List<HockeyRank> rankings = new();

            foreach (var row in table.SelectSingleNode("tbody").SelectNodes("tr"))
            {
                List<string> cells = row.SelectNodes("td").Select(x => x.InnerText.Trim()).ToList();
                string IOC = row.SelectNodes("td/a/span").Where(x => x.GetClasses().Contains("show-small")).FirstOrDefault().InnerText;

                if (!rankings.Any(x => x.IOC == IOC) && int.TryParse(cells[0], out int rank))
                {
                    rankings.Add(new HockeyRank
                    {
                        IOC = IOC,
                        Rank = rank,
                        Sport = Sport,
                        Gender = gender
                    });
                }
            }

            return rankings;
        }
    }   
}
