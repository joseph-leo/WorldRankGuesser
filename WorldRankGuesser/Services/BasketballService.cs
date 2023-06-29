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
        protected override List<BasketballRank> ParseRanks(string response)
        {
            HtmlDocument? htmlDocument = new();
            htmlDocument.LoadHtml(response);
            HtmlNode? table = htmlDocument.DocumentNode.SelectSingleNode("//table");

            List<BasketballRank> rankings = new();

            if (table != null)
            {
                foreach (var row in table.SelectSingleNode("tbody").SelectNodes("tr"))
                {
                    List<string> cells = row.SelectNodes("td").Select(x => x.InnerText.Trim()).ToList();

                    if (cells != null)
                    {
                        if (int.TryParse(cells[0].Trim('.'), out int rank))
                        {
                            rankings.Add(new BasketballRank 
                            { 
                                ISO3 = CountryUtil.IOCToISO3(cells[3]),
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
