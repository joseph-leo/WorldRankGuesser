using HtmlAgilityPack;
using System.Linq;
using System.Reflection;
using WorldRankGuesser.Data;
using WorldRankGuesser.Helpers;

namespace WorldRankGuesser.Services
{
    public class HockeyService : ScrapeService<HockeyRank, HockeyRank>
    {
        protected override List<HockeyRank> ParseRanks(string response)
        {
            return Sport switch
            {
                "Field Hockey" => ParseFieldHockey(response),
                "Ice Hockey" => ParseIceHockey(response),
                _ => new List<HockeyRank>(),
            };
        }

        private List<HockeyRank> ParseFieldHockey(string response)
        {
            List<HockeyRank> rankings = new();

            HtmlDocument? htmlDocument = new();
            htmlDocument.LoadHtml(response);
            var table = htmlDocument.DocumentNode.SelectNodes("//a").Where(x => x.GetClasses().Contains("table-row"));

            foreach (var row in table)
            {
                var rowList = row.InnerText.Split("  ").Select(x => x.Trim()).ToList();
                string ISO3 = CountryUtil.GetISO3FromCountry(rowList[1]);

                if (int.TryParse(rowList[0], out int rank))
                {
                    rankings.Add(new HockeyRank
                    {
                        ISO3 = ISO3,
                        Position = rank,
                        Sport = Sport,
                        Gender = Gender
                    });
                }
            }

            return rankings;
        }

        private List<HockeyRank> ParseIceHockey(string response)
        {
            List<HockeyRank> rankings = new();

            HtmlDocument? htmlDocument = new();
            htmlDocument.LoadHtml(response);
            var tables = htmlDocument.DocumentNode.SelectNodes("//table");

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

                if (int.TryParse(cells[0], out int rank))
                {
                    rankings.Add(new HockeyRank
                    {
                        ISO3 = CountryUtil.IOCToISO3(IOC),
                        Position = rank,
                        Sport = Sport,
                        Gender = gender
                    });
                }
            }

            return rankings;
        }
    }   
}
