using HtmlAgilityPack;
using System.Globalization;
using System.Net;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using WorldRankGuesser.Data;
using WorldRankGuesser.Helpers;

namespace WorldRankGuesser.Services
{
    public class BasketballService : ScrapeService<BasketballRank, BasketballRank>
    {
        protected override List<string> Urls
        {
            get
            {
                return new List<string>
                {
                    "https://www.fiba.basketball/rankingmen",
                    "https://www.fiba.basketball/rankingwomen"
                };
            }
        }

        protected override List<BasketballRank> ParseData(HtmlDocument htmlDoc)
        {
            HtmlNode? table = htmlDoc.DocumentNode.SelectSingleNode("//table");

            List<BasketballRank> rankings = new();

            if (table != null)
            {
                foreach (var row in table.SelectSingleNode("tbody").SelectNodes("tr"))
                {
                    List<string> cells = row.SelectNodes("td").Select(x => x.InnerText.Trim()).ToList();

                    if (cells != null)
                    {
                        if (!rankings.Any(x => x.IOC == cells[3]) && int.TryParse(cells[0].Trim('.'), out int rank))
                        {
                            rankings.Add(new BasketballRank 
                            { 
                                IOC = cells[3], 
                                Rank = rank 
                            });
                        }
                    }
                }
            }

            return rankings;
        }
    }
}
