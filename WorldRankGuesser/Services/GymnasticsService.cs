using HtmlAgilityPack;
using System.Net;
using WorldRankGuesser.Data;
using WorldRankGuesser.Helpers;

namespace WorldRankGuesser.Services
{
    public class GymnasticsService : ScrapeService<GymnasticsRank, GymnasticsRank>
    {
        protected override List<GymnasticsRank> ParseRanks(HtmlDocument htmlDoc)
        {
            List<GymnasticsRank> ranks = new();

            var tables = htmlDoc.DocumentNode.SelectNodes("//table");
            
            foreach (var table in tables )
            {
                var header = table.SelectSingleNode("thead").SelectNodes("tr");
                string _event = header[0].InnerText.Trim().Split('(')[0].Trim();

                var rows = table.SelectSingleNode("tbody").SelectNodes("tr");

                foreach(var row in rows )
                {
                    List<string> cells = row.SelectNodes("td").Select(x => x.InnerText.Trim()).ToList();
                    
                    string IOC = string.Empty;

                    if (cells[2].Contains(';'))
                        IOC = cells[2].Split(';')[1];
                    else
                        IOC = CountryUtil.GetISO3(cells[2]).ToIOC();

                    if (int.TryParse(cells[0], out int rank))
                    {
                        ranks.Add(new GymnasticsRank
                        {
                            Event = _event,
                            Sport = Sport,
                            Gender = Gender,
                            IOC = IOC,
                            Position = rank
                        });
                    }                   
                }
            }

            return ranks;
        }
    }
}
