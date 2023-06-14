using HtmlAgilityPack;
using System.Reflection;
using WorldRankGuesser.Data;
using WorldRankGuesser.Helpers;

namespace WorldRankGuesser.Services
{
    public class HockeyService : ScrapeService<HockeyRank, HockeyRank>
    {
        protected override List<HockeyRank> ParseRanks(HtmlDocument htmlDoc)
        {
            switch (Sport)
            {
                case "Field Hockey":
                    return ParseFieldHockey(htmlDoc);
                default:
                    return new List<HockeyRank>();
            }
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
    }   
}
