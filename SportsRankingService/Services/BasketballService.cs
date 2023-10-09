using HtmlAgilityPack;
using SportsRankingService.Models;
using SportsRankingService.Utilities;
using System.Globalization;
using System.Net;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;


namespace SportsRankingService.Services
{
    public class BasketballService : ScrapeService
    {
        public BasketballService(ILogger<ScrapeService> logger) : base(logger)
        {
        }

        protected override List<SportsRanking> ParseRanks(string response)
        {
            try
            {
                HtmlDocument? htmlDocument = new();
                htmlDocument.LoadHtml(response);
                HtmlNode? table = htmlDocument.DocumentNode.SelectSingleNode("//table");

                List<SportsRanking> rankings = new();

                if (table != null)
                {
                    foreach (var row in table.SelectSingleNode("tbody").SelectNodes("tr"))
                    {
                        List<string> cells = row.SelectNodes("td").Select(x => x.InnerText.Trim()).ToList();

                        if (cells != null)
                        {
                            if (short.TryParse(cells[0].Trim('.'), out short rank))
                            {
                                string _ISO3 = CountryUtil.IOCToISO3(cells[3]);

                                rankings.Add(new SportsRanking
                                {
                                    ISO3 = _ISO3,
                                    Position = rank,
                                    Sport = Sport,
                                    Gender = Gender,
                                    RankDate = DateTime.Now,
                                    CountryName = GetCountryName(_ISO3)
                                });
                            }
                        }
                    }
                }

                return rankings;
            }
            catch (Exception ex)
            {
                Log(ex, _logger);
                return new List<SportsRanking>();
            }
            
        }
    }
}
