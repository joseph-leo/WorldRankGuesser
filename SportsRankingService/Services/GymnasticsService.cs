using HtmlAgilityPack;
using SportsRankingService.Models;
using SportsRankingService.Utilities;
using System.Net;

namespace SportsRankingService.Services
{
    public class GymnasticsService : ScrapeService
    {
        public GymnasticsService(ILogger<GymnasticsService> logger) : base(logger)
        {
        }

        protected override List<SportsRanking> ParseRanks(string response)
        {
            try
            {
                List<SportsRanking> ranks = new();

                HtmlDocument? htmlDocument = new();
                htmlDocument.LoadHtml(response);
                var tables = htmlDocument.DocumentNode.SelectNodes("//table");

                foreach (var table in tables)
                {
                    var header = table.SelectSingleNode("thead").SelectNodes("tr");
                    string _event = header[0].InnerText.Trim().Split('(')[0].Trim();

                    var rows = table.SelectSingleNode("tbody").SelectNodes("tr");

                    foreach (var row in rows)
                    {
                        List<string> cells = row.SelectNodes("td").Select(x => x.InnerText.Trim()).ToList();

                        string ISO3 = string.Empty;

                        if (cells[2].Contains(';'))
                        {
                            string IOC = cells[2].Split(';')[1];
                            ISO3 = CountryUtil.IOCToISO3(IOC);
                        }
                        else
                        {
                            string countryName = cells[2];
                            ISO3 = CountryUtil.GetISO3FromCountry(countryName);
                        }
                            

                        if (short.TryParse(cells[0], out short rank))
                        {
                            ranks.Add(new SportsRanking
                            {
                                Event = _event,
                                Sport = Sport,
                                Gender = Gender,
                                ISO3 = ISO3,
                                Position = rank,
                                RankDate = DateTime.Now,
                                CountryName = CountryUtil.GetCountryName(ISO3)
                            });
                        }
                    }
                }

                return ranks;
            }
            catch (Exception ex)
            {
                //_logHelper.Log(ex);
                return new List<SportsRanking>();
            }
            
        }
    }
}
