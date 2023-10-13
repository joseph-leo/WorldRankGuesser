using HtmlAgilityPack;
using SportsRankingService.Models;
using SportsRankingService.Utilities;
using System.Linq;
using System.Reflection;

namespace SportsRankingService.Services
{
    public class HockeyService : ScrapeService
    {
        public HockeyService(ILogger<HockeyService> logger) : base(logger)
        {
        }

        protected override List<SportsRanking> ParseRanks(string response)
        {
            try
            {
                return Sport switch
                {
                    "Field Hockey" => ParseFieldHockey(response),
                    "Ice Hockey" => ParseIceHockey(response),
                    _ => new List<SportsRanking>(),
                };
            }
            catch (Exception ex)
            {
                //_logHelper.Log(ex);
                return new List<SportsRanking>();
            }
            
        }

        private List<SportsRanking> ParseFieldHockey(string response)
        {
            List<SportsRanking> rankings = new();

            HtmlDocument? htmlDocument = new();
            htmlDocument.LoadHtml(response);
            var table = htmlDocument.DocumentNode.SelectNodes("//a").Where(x => x.GetClasses().Contains("table-row"));

            foreach (var row in table)
            {
                var rowList = row.InnerText.Split("  ").Select(x => x.Trim()).ToList();
                string ISO3 = CountryUtil.GetISO3FromCountry(rowList[1]);

                if (short.TryParse(rowList[0], out short rank))
                {
                    rankings.Add(new SportsRanking
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

        private List<SportsRanking> ParseIceHockey(string response)
        {
            List<SportsRanking> rankings = new();

            HtmlDocument? htmlDocument = new();
            htmlDocument.LoadHtml(response);
            var tables = htmlDocument.DocumentNode.SelectNodes("//table");

            HtmlNode mensTable = tables[0];
            rankings.AddRange(ParseIceHockeyTable(mensTable, "Men"));

            HtmlNode womensTable = tables[1];
            rankings.AddRange(ParseIceHockeyTable(womensTable, "Women"));

            return rankings;
        }

        private List<SportsRanking> ParseIceHockeyTable(HtmlNode table, string gender)
        {
            List<SportsRanking> rankings = new();

            foreach (var row in table.SelectSingleNode("tbody").SelectNodes("tr"))
            {
                List<string> cells = row.SelectNodes("td").Select(x => x.InnerText.Trim()).ToList();
                HtmlNode? node = row.SelectNodes("td/a/span").Where(x => x.GetClasses().Contains("show-small")).FirstOrDefault();
                string IOC = node.InnerText;

                if (short.TryParse(cells[0], out short rank))
                {
                    rankings.Add(new SportsRanking
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
