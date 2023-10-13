using HtmlAgilityPack;
using SportsRankingService.Models;
using SportsRankingService.Utilities;
using System.Globalization;
using System.Reflection;


namespace SportsRankingService.Services
{
    public class CricketService : ScrapeService
    {
        public CricketService(ILogger<CricketService> logger) : base(logger)
        {
        }

        protected override List<SportsRanking> ParseRanks(string response)
        {
            try
            {
                List<SportsRanking> rankings = new();

                HtmlDocument? htmlDocument = new();
                htmlDocument.LoadHtml(response);

                var table = htmlDocument.DocumentNode.SelectSingleNode("//table");
                var rows = table.SelectSingleNode("tbody").SelectNodes("tr");

                foreach (var row in rows)
                {
                    List<string> cells = row.SelectNodes("td").Select(x => x.InnerText.Trim()).ToList();
                    string countryName = cells[1].Split('\n')[0].Trim();

                    if (short.TryParse(cells[0], out short position))
                    {
                        if (countryName == "West Indies")
                        {
                            rankings.Add(new SportsRanking
                            {
                                Gender = Gender,
                                Sport = Sport,
                                ISO3 = "WI",
                                Position = position,
                                RankDate = DateTime.Now,
                                CountryName = countryName
                            });
                        }
                        else
                        {
                            string ISO3 = CountryUtil.GetISO3FromCountry(countryName);

                            rankings.Add(new SportsRanking
                            {
                                Gender = Gender,
                                Sport = Sport,
                                ISO3 = ISO3,
                                Position = position,
                                CountryName = countryName                              
                            });
                        }
                    }

                }
                return rankings;
            }
            catch (Exception ex)
            {
                //_logHelper.Log(ex);
                return new List<SportsRanking>();
            }
        }
    }
}
