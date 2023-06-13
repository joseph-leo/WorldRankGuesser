using HtmlAgilityPack;
using WorldRankGuesser.Data;
using WorldRankGuesser.Helpers;

namespace WorldRankGuesser.Services
{
    public class HockeyService : ScrapeService<HockeyRank, HockeyRank>
    {
        //protected override List<string>? Urls
        //{
        //    get
        //    {
        //        return new List<string>
        //        {
        //            "https://www.fih.hockey/outdoor-hockey-rankings",
        //            "wwwroot/remotehtml/outdoor-hockey-rankings-women.html"
        //        };
        //    }
        //}

        protected override List<HockeyRank> ParseData(HtmlDocument htmlDoc)
        {
            List<HockeyRank> rankings = new();
            var table = htmlDoc.DocumentNode.SelectNodes("//a").Where(x => x.GetClasses().Contains("table-row"));

            foreach (var row in table)
            {
                var rowList = row.InnerText.Split("  ").Select(x => x.Trim()).ToList();
                string ISO3 = CountryUtilities.GetISO3(rowList[1]);

                if (!rankings.Any(x => x.ISO3 == ISO3) && int.TryParse(rowList[0], out int rank))
                {
                    rankings.Add(new HockeyRank
                    {
                        ISO3 = ISO3,
                        Rank = rank
                    });
                }
            }

            return rankings;
        }
    }

    //private List<HockeyRank> ParseFieldHockey(HtmlDocument htmlDoc)
    //{

    //}
}
